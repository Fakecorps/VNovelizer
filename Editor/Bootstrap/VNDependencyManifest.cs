using System;
using System.IO;
using System.Text;
using LitJson;

namespace VNovelizer.Editor.Bootstrap
{
    internal static class VNDependencyManifest
    {
        internal const string PackageName = "com.kyrylokuzyk.primetween";
        internal const string PackageVersion = "1.4.11";
        internal const string RegistryUrl = "https://registry.npmjs.org";
        private const string Scope = "com.kyrylokuzyk";

        internal static string Merge(string content, out bool changed)
        {
            changed = false;
            var root = JsonMapper.ToObject(content);
            if (root == null || !root.IsObject || !root.ContainsKey("dependencies") ||
                root["dependencies"] == null || !root["dependencies"].IsObject)
                throw new InvalidDataException("manifest.json 缺少有效的 dependencies 对象，未修改文件。");

            var dependencies = root["dependencies"];
            bool needsRegistry = true;
            if (dependencies.ContainsKey(PackageName))
            {
                var source = dependencies[PackageName];
                if (source == null || !source.IsString)
                    throw new InvalidDataException("PrimeTween 依赖地址不是字符串，未修改文件。");
                string value = (string)source;
                if (TryGetVersion(value, out _))
                {
                    if (!IsCompatibleVersion(value))
                        throw new InvalidOperationException("项目指定的 PrimeTween 版本低于 " + PackageVersion +
                            "，已保留原配置。请在 Package Manager 中更新后重试。");
                }
                else
                {
                    needsRegistry = false;
                }
            }

            if (needsRegistry)
            {
                var registries = root.ContainsKey("scopedRegistries") ? root["scopedRegistries"] : NewArray();
                if (registries == null || !registries.IsArray)
                    throw new InvalidDataException("scopedRegistries 不是数组，未修改文件。");

                JsonData npm = null;
                bool hasScope = false;
                foreach (JsonData registry in registries)
                {
                    if (registry == null || !registry.IsObject || !registry.ContainsKey("url") ||
                        registry["url"] == null || !registry["url"].IsString ||
                        !registry.ContainsKey("scopes") || registry["scopes"] == null || !registry["scopes"].IsArray)
                        throw new InvalidDataException("存在无效的 scoped registry，未修改文件。");

                    string url = ((string)registry["url"]).TrimEnd('/');
                    if (url == RegistryUrl) npm = registry;
                    foreach (JsonData scope in registry["scopes"])
                    {
                        if (scope == null || !scope.IsString)
                            throw new InvalidDataException("Registry scope 不是字符串，未修改文件。");
                        string existing = (string)scope;
                        if (Scope == existing || Scope.StartsWith(existing + ".", StringComparison.Ordinal) ||
                            existing == PackageName)
                            hasScope = true;
                    }
                }

                if (!hasScope)
                {
                    if (npm == null)
                    {
                        npm = new JsonData();
                        npm["name"] = "VNovelizer npm";
                        npm["url"] = RegistryUrl;
                        npm["scopes"] = NewArray();
                        registries.Add(npm);
                    }
                    npm["scopes"].Add(Scope);
                    root["scopedRegistries"] = registries;
                    changed = true;
                }
            }

            if (!dependencies.ContainsKey(PackageName))
            {
                dependencies[PackageName] = PackageVersion;
                changed = true;
            }
            if (!changed) return content;

            var writer = new JsonWriter { PrettyPrint = true, IndentValue = 2 };
            root.ToJson(writer);
            string result = writer.ToString();
            if (content.Contains("\r\n")) result = result.Replace("\r\n", "\n").Replace("\n", "\r\n");
            JsonMapper.ToObject(result);
            return result + (content.Contains("\r\n") ? "\r\n" : "\n");
        }

        internal static bool Ensure(string manifestPath)
        {
            string original = File.ReadAllText(manifestPath);
            string updated = Merge(original, out bool changed);
            if (!changed) return false;

            string temporaryPath = manifestPath + ".vnovelizer.tmp~";
            try
            {
                File.WriteAllText(temporaryPath, updated, new UTF8Encoding(false));
                if (File.ReadAllText(manifestPath) != original)
                    throw new IOException("manifest.json 已被其他操作修改，请稍后重试。");
                File.Replace(temporaryPath, manifestPath, manifestPath + ".vnovelizer.bak");
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            return true;
        }

        internal static bool IsCompatibleVersion(string value)
        {
            if (!TryGetVersion(value, out var version)) return false;
            var minimum = new Version(PackageVersion);
            return version > minimum || (version == minimum && !value.Split('+')[0].Contains("-"));
        }

        private static bool TryGetVersion(string value, out Version version)
        {
            version = null;
            return !string.IsNullOrEmpty(value) && Version.TryParse(value.Split('+')[0].Split('-')[0], out version);
        }

        private static JsonData NewArray()
        {
            var array = new JsonData();
            array.SetJsonType(JsonType.Array);
            return array;
        }
    }
}
