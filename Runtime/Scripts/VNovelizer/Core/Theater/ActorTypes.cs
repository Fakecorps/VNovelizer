using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 演员类别：背景 / 角色 / 特效
    /// </summary>
    public enum ActorKind
    {
        Background,
        Character,
        Effect
    }

    /// <summary>
    /// 背景切换过渡类型（bgtrans 命令的 type 参数）。
    /// 数值与 VNovelizer/BGTransition 着色器的 _Mode 严格一致，禁止重排。
    /// </summary>
    public enum BgTransitionType
    {
        /// <summary>交叉淡化（新图 alpha 渐变）</summary>
        Fade = 0,
        /// <summary>竖向百叶窗（条带内从上到下、条带间左→右相位）</summary>
        Blinds = 1,
        /// <summary>从左到右擦除</summary>
        Wipe = 2,
        /// <summary>圆心扩散（新图从画面中心的圆逐渐扩大）</summary>
        Iris = 3,
        /// <summary>从下往上卷开</summary>
        Scroll = 4,
        /// <summary>噪点阈值溶解</summary>
        Dissolve = 5,
    }

    /// <summary>
    /// 演员外观：一次"换装/换图/换背景"的完整描述。
    /// id 使用剧本语义（如 "Amy#uniform#Smile"、BG 资源名）；
    /// sprite/texture 二选一，texture 供视频、外部纹理等后续能力使用。
    /// </summary>
    [Serializable]
    public class ActorAppearance
    {
        public string id;
        public Sprite sprite;
        public Texture2D texture;

        /// <summary>
        /// 动态立绘（Live2D 等）的角色配置引用。
        /// 本字段只承载核心自有类型 CharacterProfile——动态演员（如 L2DActor）拿到后
        /// 自行 cast 为具体子类读取其专属配置，核心零外部依赖。
        /// </summary>
        public CharacterProfile profile;

        /// <summary>
        /// 现场登台时的一次性动作指令（仅动态立绘使用，如 L2D 立绘列 角色ID#动作#表情 的动作段）。
        /// 只由 TheaterManager.OnShowCharacter 的现场路径填充；读档/快进重建的
        /// ResolveAppearance 路径不填充——动作是瞬态表演，不随存档重播。
        /// ActorState 只持久化 id 字符串，本字段天然不进存档。
        /// </summary>
        public string showMotionId;

        public ActorAppearance(string id, Sprite sprite)
        {
            this.id = id;
            this.sprite = sprite;
        }

        public ActorAppearance(string id, Texture2D texture)
        {
            this.id = id;
            this.texture = texture;
        }

        public ActorAppearance(string id, CharacterProfile profile)
        {
            this.id = id;
            this.profile = profile;
        }
    }

    /// <summary>
    /// 演员状态（可序列化）——剧场层的"剧本语义"快照。
    /// position 使用剧本像素语义（1920x1080 参考，原点为画面中心，+x 右 +y 上）；
    /// 渲染实现负责换算到自身坐标系（MeshActor: 1px = 0.01 世界单位）。
    /// </summary>
    [Serializable]
    public class ActorState
    {
        public string actorId;
        public ActorKind kind = ActorKind.Character;

        public string appearance;        // "Amy#uniform#Smile" / BG 资源名
        public Vector2 position;         // 剧本像素语义
        public float scale = 1f;
        public float scaleX = 1f;        // 翻转（沿用现有存档字段语义）
        public int zOrder;
        public float alpha = 1f;
        public bool visible = true;

        public ActorState() { }

        public ActorState(string actorId, ActorKind kind)
        {
            this.actorId = actorId;
            this.kind = kind;
        }
    }

    /// <summary>
    /// 相机状态（可序列化）。
    /// 注意：offset/rotation 为世界单位/欧拉角（引擎层语义）；
    /// 剧本命令（camerapan 等）的像素参数由命令层负责换算后写入。
    /// </summary>
    [Serializable]
    public class CameraState
    {
        public Vector3 offset;           // 相机位置偏移（世界单位，z 负 = 推近）
        public float zoom = 1f;          // 1 = 默认；>1 放大（正交尺寸缩小）
        public Vector3 rotation;         // 欧拉角
        public bool orthographic = true;
        public List<string> activeFxComponents = new List<string>();   // 相机上启用的后处理组件类型名

        /// <summary>恢复默认状态</summary>
        public void Reset()
        {
            offset = Vector3.zero;
            zoom = 1f;
            rotation = Vector3.zero;
            orthographic = true;
            activeFxComponents.Clear();
        }
    }
}
