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
