using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VNovelizer.Core.Commands.Meta
{
    /// <summary>
    /// 单个参数的节点化描述（<see cref="VNParamAttribute"/> 的运行时投影）。
    /// </summary>
    public class VNParamInfo
    {
        public int Index;
        public string Name;
        public VNParamType Type;
        public string Description;
        public string[] Options = Array.Empty<string>();
        public float Min = float.NaN;
        public float Max = float.NaN;
        public string Default;
        public bool Optional;
        public bool ImplicitBinding;
        public string BoundColumn;
        public bool InlineForbidden;

        public bool HasRange => !float.IsNaN(Min) && !float.IsNaN(Max);

        /// <summary>用于命令面板的签名片段（如 `dur` / `[str]` / `[name]📎`）</summary>
        public string ToSignatureToken()
        {
            string token = Name;
            if (ImplicitBinding) token += "*";
            return Optional || ImplicitBinding ? "[" + token + "]" : token;
        }
    }

    /// <summary>
    /// 单个命令的完整节点化描述：声明式元数据（特性）+ 反射推导的行为特征。
    /// </summary>
    public class VNCommandInfo
    {
        /// <summary>注册名（小写，即 Command 列中书写的名字）</summary>
        public string Name;

        /// <summary>命令实现类型</summary>
        public Type ImplType;

        /// <summary>是否标注了 <see cref="VNCommandMetaAttribute"/>。为 false 时图编辑器渲染为「通用节点」</summary>
        public bool HasMeta;

        public VNCommandCategory Category = VNCommandCategory.Misc;
        public string Description = "";
        public char ArgSeparator = ',';
        public bool VariadicArgs;
        public bool Planned;

        /// <summary>按 Index 升序的参数列表（未标注时为空）</summary>
        public List<VNParamInfo> Parameters = new List<VNParamInfo>();

        // ---- 以下为反射推导，绝不由特性声明（避免重复信息源） ----

        /// <summary>是否 override 了 <c>ExecuteAsync</c>（异步命令，链中会阻塞至完成）</summary>
        public bool IsAsync;

        /// <summary>是否 override 了 <c>Simulate</c>（无则读档/快进时状态可能不一致）</summary>
        public bool HasSimulate;

        /// <summary>是否 override 了 <c>Interrupt</c>（点击跳过时能否快进到最终态）</summary>
        public bool HasInterrupt;

        /// <summary>是否流程命令（查 <c>ChainParser.IsFlowCommand</c>，仅可置于链尾）</summary>
        public bool IsFlowCommand;

        /// <summary>命令面板显示的签名，如 `shake(target,dur[,str])`</summary>
        public string Signature
        {
            get
            {
                if (Parameters.Count == 0)
                    return HasMeta ? Name + "()" : Name + "(…)";
                return Name + "(" + string.Join(",", Parameters.Select(p => p.ToSignatureToken())) + ")";
            }
        }

        /// <summary>取指定下标的参数描述，越界返回 null（可变长命令的溢出参数）</summary>
        public VNParamInfo GetParam(int index)
        {
            return Parameters.FirstOrDefault(p => p.Index == index);
        }
    }

    /// <summary>
    /// 命令节点化元数据的反射读取器（带缓存）。
    ///
    /// <para>
    /// <b>职责边界</b>：只负责"把特性 + 反射特征读成 <see cref="VNCommandInfo"/>"。
    /// 不做 UI、不做校验、不感知 GraphView——因此可被图编辑器、命令面板、
    /// 剧本校验器、文档生成器等多方复用。
    /// </para>
    ///
    /// <para>
    /// 数据来源是 <c>CommandManager.EnumerateRegisteredCommands()</c>，
    /// 因此**通过反射自动注册的第三方自定义命令同样在列**。
    /// </para>
    /// </summary>
    public static class CommandMetaReader
    {
        private static Dictionary<string, VNCommandInfo> _cache;

        /// <summary>
        /// 获取全部命令的节点化描述（首次调用时构建缓存）。
        /// key 为小写命令名。
        /// </summary>
        public static IReadOnlyDictionary<string, VNCommandInfo> GetAll()
        {
            if (_cache == null) Build();
            return _cache;
        }

        /// <summary>按命令名查描述（大小写不敏感），未注册返回 null。</summary>
        public static VNCommandInfo Get(string commandName)
        {
            if (string.IsNullOrEmpty(commandName)) return null;
            if (_cache == null) Build();
            _cache.TryGetValue(commandName.ToLower(), out var info);
            return info;
        }

        /// <summary>按分类分组，供命令面板构建（分类内按命令名排序）。</summary>
        public static Dictionary<VNCommandCategory, List<VNCommandInfo>> GetGrouped()
        {
            if (_cache == null) Build();
            return _cache.Values
                .GroupBy(i => i.Category)
                .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Name).ToList());
        }

        /// <summary>
        /// 清空缓存。热重载 / 新注册命令 / Editor 重新编译后调用。
        /// </summary>
        public static void Invalidate()
        {
            _cache = null;
        }

        private static void Build()
        {
            _cache = new Dictionary<string, VNCommandInfo>();

            var manager = CommandManager.GetInstance();
            manager.EnsureInitialized();

            foreach (var pair in manager.EnumerateRegisteredCommands())
            {
                var instance = pair.Value;
                if (instance == null) continue;

                try
                {
                    _cache[pair.Key] = ReadFrom(pair.Key, instance);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CommandMetaReader] 读取命令 '{pair.Key}' 元数据失败：{e.Message}");
                    // 降级为无元数据（通用节点态），不影响其余命令
                    _cache[pair.Key] = new VNCommandInfo
                    {
                        Name = pair.Key,
                        ImplType = instance.GetType(),
                        HasMeta = false
                    };
                }
            }
        }

        private static VNCommandInfo ReadFrom(string name, VNCommand instance)
        {
            Type type = instance.GetType();
            var info = new VNCommandInfo { Name = name, ImplType = type };

            // ---- 声明式元数据 ----
            var meta = type.GetCustomAttribute<VNCommandMetaAttribute>(inherit: false);
            if (meta != null)
            {
                info.HasMeta = true;
                info.Category = meta.Category;
                info.Description = meta.Description;
                info.ArgSeparator = meta.ArgSeparator;
                info.VariadicArgs = meta.VariadicArgs;
                info.Planned = meta.Planned;
            }

            // [VNParam] 可标在类上或 CommandName 属性上，两处都收集
            var paramAttrs = new List<VNParamAttribute>();
            paramAttrs.AddRange(type.GetCustomAttributes<VNParamAttribute>(inherit: false));

            var cmdNameProp = type.GetProperty("CommandName",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (cmdNameProp != null)
                paramAttrs.AddRange(cmdNameProp.GetCustomAttributes<VNParamAttribute>(inherit: false));

            foreach (var pa in paramAttrs.OrderBy(p => p.Index))
            {
                info.Parameters.Add(new VNParamInfo
                {
                    Index = pa.Index,
                    Name = pa.Name,
                    Type = pa.Type,
                    Description = pa.Description,
                    Options = pa.GetOptions(),
                    Min = pa.Min,
                    Max = pa.Max,
                    Default = pa.Default,
                    Optional = pa.Optional,
                    ImplicitBinding = pa.ImplicitBinding,
                    BoundColumn = pa.BoundColumn,
                    InlineForbidden = pa.InlineForbidden,
                });
            }

            // ---- 反射推导的行为特征（绝不由特性声明）----
            info.IsAsync = IsOverridden(type, "ExecuteAsync", typeof(string));
            info.HasSimulate = IsOverridden(type, "Simulate", typeof(string));
            info.HasInterrupt = IsOverridden(type, "Interrupt");
            info.IsFlowCommand = Chain.ChainParser.IsFlowCommand(name);

            return info;
        }

        /// <summary>
        /// 判断子类是否真正 override 了 <see cref="VNCommand"/> 的某个虚方法
        /// （而非继承基类默认实现）。
        /// </summary>
        private static bool IsOverridden(Type type, string methodName, params Type[] argTypes)
        {
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, argTypes ?? Type.EmptyTypes, null);

            // DeclaringType 仍是基类 → 未重写
            return method != null && method.DeclaringType != typeof(VNCommand);
        }
    }
}
