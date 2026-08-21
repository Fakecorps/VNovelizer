using System.Collections;
using UnityEngine;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 世界空间 quad 演员实现：由专用场景相机拍摄。
    ///
    /// 坐标换算契约：
    /// - 1 剧本像素 = 0.01 世界单位（1920x1080 → 19.2x10.8）；
    /// - 剧本像素原点 = 画面中心（与 UI 槽位居中锚点语义一致）；
    /// - zOrder → 世界 Z：z = -0.1 * zOrder（zOrder 越大越靠前，即越靠近相机）。
    ///
    /// 网格按 Sprite 尺寸原生构建（含 pivot 偏移与图集 UV 修正），
    /// 着色器使用 Sprites/Default（Built-in 与 URP 均可渲染，支持透明）。
    /// </summary>
    public class MeshActor : IActor
    {
        /// <summary>1 剧本像素对应的世界单位数</summary>
        public const float PixelsToWorld = 0.01f;
        /// <summary>每个 zOrder 级对应的世界 Z 间距（负方向 = 靠近相机）</summary>
        public const float DepthStep = -0.1f;

        public string ActorId { get; }
        public ActorKind Kind { get; }

        public bool IsValid => _go != null;

        private readonly GameObject _go;
        private readonly MeshFilter _meshFilter;
        private readonly MeshRenderer _renderer;
        private readonly Material _material;

        // 当前视觉状态缓存（供动画与归位使用）
        private float _scale = 1f;
        private bool _flipped;
        private int _zOrder;
        private float _alpha = 1f;
        private bool _visible = true;

        // 活动动画句柄与终值（Interrupt 时瞬间到终态）
        // 注意：终值初值必须与视觉初值一致——否则"未播过动画就 Interrupt"会把演员
        // 打到 alpha=0（不可见）或原点，这类回跳极难排查。
        private Coroutine _fadeRoutine;
        private float _pendingFadeTarget = 1f;
        private Coroutine _moveRoutine;
        private Vector2 _pendingMoveTargetPx = Vector2.zero;

        /// <summary>本演员自建的网格（换外观时需显式销毁，否则运行时 Mesh 泄漏）</summary>
        private Mesh _ownedMesh;

        private static Shader _cachedShader;

        public MeshActor(string actorId, ActorKind kind, Transform parent)
        {
            ActorId = actorId;
            Kind = kind;

            _go = new GameObject($"Actor_{kind}_{actorId}");
            _go.transform.SetParent(parent, false);
            _go.layer = 0; // Default 层（剧场相机渲染；UI 层由 Overlay Canvas 负责）

            _meshFilter = _go.AddComponent<MeshFilter>();
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            _material = new Material(ResolveShader());
            _renderer.material = _material;
        }

        #region 外观

        public void SetAppearance(ActorAppearance appearance)
        {
            if (!IsValid)
            {
                Debug.LogWarning($"[MeshActor] {ActorId} 渲染对象已销毁，忽略 SetAppearance");
                return;
            }

            if (appearance?.sprite != null)
            {
                AssignMesh(BuildQuadMesh(appearance.sprite));
                _material.mainTexture = appearance.sprite.texture;
            }
            else if (appearance?.texture != null)
            {
                AssignMesh(BuildQuadMesh(appearance.texture.width, appearance.texture.height, Vector2.one * 0.5f));
                _material.mainTexture = appearance.texture;
            }
            else
            {
                // 无外观：清空网格（保持对象存活，仅不渲染）
                AssignMesh(null);
                if (!string.IsNullOrEmpty(appearance?.id))
                    Debug.LogWarning($"[MeshActor] {ActorId} 外观 '{appearance.id}' 无可用 Sprite/Texture");
            }

            ApplyTransform();
        }

        /// <summary>
        /// 挂载新网格并销毁上一张自建网格。
        /// 运行时 new Mesh() 不会被 GC 自动回收（需 Resources.UnloadUnusedAssets），
        /// 每次换表情都会残留一张网格，长剧本累积可观——必须显式销毁。
        /// </summary>
        private void AssignMesh(Mesh mesh)
        {
            if (_ownedMesh != null && _ownedMesh != mesh)
                Object.Destroy(_ownedMesh);

            _ownedMesh = mesh;
            _meshFilter.sharedMesh = mesh;
        }

        #endregion

        #region 变换

        public void SetPosition(Vector2 posPx)
        {
            _pendingMoveTargetPx = posPx; // 同步终值，避免后续 Interrupt 回跳
            SetLocalXY(posPx.x * PixelsToWorld, posPx.y * PixelsToWorld);
        }

        public void SetScale(float scale)
        {
            _scale = Mathf.Max(scale, 0.0001f);
            ApplyTransform();
        }

        public void SetFlip(bool flipped)
        {
            _flipped = flipped;
            ApplyTransform();
        }

        public void SetDepth(int zOrder)
        {
            _zOrder = zOrder;
            if (!IsValid) return;
            var pos = _go.transform.localPosition;
            pos.z = zOrder * DepthStep;
            _go.transform.localPosition = pos;
        }

        private void SetLocalXY(float x, float y)
        {
            if (!IsValid) return;
            var pos = _go.transform.localPosition;
            pos.x = x;
            pos.y = y;
            _go.transform.localPosition = pos;
        }

        private void ApplyTransform()
        {
            if (!IsValid) return;
            _go.transform.localScale = new Vector3(_scale * (_flipped ? -1f : 1f), _scale, 1f);
        }

        #endregion

        #region 可见性

        public void SetAlpha(float alpha)
        {
            _alpha = Mathf.Clamp01(alpha);
            _pendingFadeTarget = _alpha;
            ApplyColor();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (IsValid) _go.SetActive(_visible);
        }

        private void ApplyColor()
        {
            if (_material != null)
                _material.color = new Color(1f, 1f, 1f, _alpha);
        }

        #endregion

        #region 转场与动画

        /// <summary>
        /// 转场：当前阶段为立即切换实现（转场着色器系统在后续阶段接入，
        /// 届时按 transitionName 启用材质着色器变体；交叉淡化为缺省语义）。
        /// </summary>
        public void Transition(ActorAppearance next, string transitionName,
                               float duration, float[] parameters)
        {
            // TODO(Theater 阶段6): transitionName → 材质着色器变体（Crossfade/Dissolve/...）
            SetAppearance(next);
        }

        public IEnumerator FadeAsync(float targetAlpha, float duration)
        {
            if (!IsValid) yield break;
            InterruptFade();

            targetAlpha = Mathf.Clamp01(targetAlpha);
            _pendingFadeTarget = targetAlpha;

            if (duration <= 0f)
            {
                SetAlpha(targetAlpha);
                yield break;
            }

            float start = _alpha;
            _fadeRoutine = MonoManager.GetInstance().StartCoroutine(RunFade(start, targetAlpha, duration));
            // RunFade 由句柄驱动，这里返回一个等待句柄结束的迭代器
            yield return new WaitUntil(() => _fadeRoutine == null);
        }

        private IEnumerator RunFade(float start, float target, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (!IsValid) yield break;
                elapsed += Time.deltaTime;
                _alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                ApplyColor();
                yield return null;
            }
            _alpha = target;
            ApplyColor();
            _fadeRoutine = null;
        }

        public IEnumerator MoveAsync(Vector2 targetPx, float duration)
        {
            if (!IsValid) yield break;
            InterruptMove();

            _pendingMoveTargetPx = targetPx;

            if (duration <= 0f)
            {
                SetPosition(targetPx);
                yield break;
            }

            Vector2 startWorld = new Vector2(_go.transform.localPosition.x, _go.transform.localPosition.y);
            Vector2 targetWorld = targetPx * PixelsToWorld;
            _moveRoutine = MonoManager.GetInstance().StartCoroutine(RunMove(startWorld, targetWorld, duration));
            yield return new WaitUntil(() => _moveRoutine == null);
        }

        private IEnumerator RunMove(Vector2 startWorld, Vector2 targetWorld, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (!IsValid) yield break;
                elapsed += Time.deltaTime;
                Vector2 p = Vector2.Lerp(startWorld, targetWorld, Mathf.Clamp01(elapsed / duration));
                SetLocalXY(p.x, p.y);
                yield return null;
            }
            SetLocalXY(targetWorld.x, targetWorld.y);
            _moveRoutine = null;
        }

        /// <summary>中断全部动画并瞬间到终态（跳过/快进时调用）</summary>
        public void Interrupt()
        {
            InterruptFade();
            InterruptMove();
        }

        private void InterruptFade()
        {
            if (_fadeRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }
            _alpha = _pendingFadeTarget;
            ApplyColor();
        }

        private void InterruptMove()
        {
            if (_moveRoutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_moveRoutine);
                _moveRoutine = null;
            }
            Vector2 targetWorld = _pendingMoveTargetPx * PixelsToWorld;
            SetLocalXY(targetWorld.x, targetWorld.y);
        }

        #endregion

        #region 网格构建

        /// <summary>
        /// 按 Sprite 尺寸构建居中 quad 网格（含 pivot 偏移与图集 UV 修正）。
        /// 法线朝 -Z（相机位于 -Z 侧朝 +Z 观看）。
        /// </summary>
        private static Mesh BuildQuadMesh(Sprite sprite)
        {
            Rect rect = sprite.textureRect; // 图集安全：使用纹理内实际矩形
            return BuildQuadMesh(rect.width, rect.height,
                sprite.pivot / rect.size, // pivot 归一化（Unity pivot 以像素存储）
                sprite.texture.width, sprite.texture.height, rect);
        }

        private static Mesh BuildQuadMesh(float widthPx, float heightPx, Vector2 normalizedPivot)
        {
            return BuildQuadMesh(widthPx, heightPx, normalizedPivot, (int)widthPx, (int)heightPx,
                new Rect(0, 0, widthPx, heightPx));
        }

        private static Mesh BuildQuadMesh(float widthPx, float heightPx, Vector2 normalizedPivot,
                                          int texWidth, int texHeight, Rect texRect)
        {
            float w = widthPx * PixelsToWorld;
            float h = heightPx * PixelsToWorld;

            // pivot 偏移：让 pivot 点落在局部原点（与 Image 的 pivot 行为一致）
            float ox = (normalizedPivot.x - 0.5f) * w;
            float oy = (normalizedPivot.y - 0.5f) * h;

            // UV（图集安全：矩形归一化到整张纹理）
            float u0 = texRect.x / texWidth;
            float v0 = texRect.y / texHeight;
            float u1 = (texRect.x + texRect.width) / texWidth;
            float v1 = (texRect.y + texRect.height) / texHeight;

            float hw = w * 0.5f, hh = h * 0.5f;
            var mesh = new Mesh { name = "ActorQuad" };

            mesh.vertices = new[]
            {
                new Vector3(-hw + ox, -hh + oy, 0f), // 0 左下
                new Vector3( hw + ox, -hh + oy, 0f), // 1 右下
                new Vector3(-hw + ox,  hh + oy, 0f), // 2 左上
                new Vector3( hw + ox,  hh + oy, 0f), // 3 右上
            };
            mesh.uv = new[]
            {
                new Vector2(u0, v0),
                new Vector2(u1, v0),
                new Vector2(u0, v1),
                new Vector2(u1, v1),
            };
            mesh.normals = new[]
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            };
            // 从 -Z 侧（相机侧）看为顺时针绕序
            mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Shader ResolveShader()
        {
            if (_cachedShader != null) return _cachedShader;

            // Sprites/Default：Built-in 与 URP 均可渲染、支持透明与主纹理
            _cachedShader = Shader.Find("Sprites/Default");
            if (_cachedShader == null) _cachedShader = Shader.Find("Unlit/Transparent");
            if (_cachedShader == null)
                Debug.LogError("[MeshActor] 找不到可用着色器（Sprites/Default / Unlit/Transparent），演员将无法正确渲染");
            return _cachedShader;
        }

        #endregion

        /// <summary>销毁渲染对象（状态由 TheaterManager 持有，可重建）</summary>
        public void Dispose()
        {
            Interrupt();
            if (_ownedMesh != null) { Object.Destroy(_ownedMesh); _ownedMesh = null; }
            if (_go != null) Object.Destroy(_go);
            if (_material != null) Object.Destroy(_material);
        }
    }
}
