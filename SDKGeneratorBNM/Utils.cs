using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    public static class Utils
    {
        public static readonly HashSet<string> ReservedTypeNames = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> PrimitiveTypeMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "System.Void", "void" },
            { "System.Byte", "uint8_t" },
            { "System.SByte", "int8_t" },
            { "System.Int16", "int16_t" },
            { "System.Int32", "int" },
            { "System.Int64", "int64_t" },
            { "System.Single", "float" },
            { "System.Double", "double" },
            { "System.Boolean", "bool" },
            { "System.Char", "char" },
            { "System.UInt16", "uint16_t" },
            { "System.UInt32", "uint32_t" },
            { "System.UInt64", "uint64_t" },
            { "System.String", "::BNM::Structures::Mono::String*" },
            { "System.Type", "::BNM::MonoType*" },
            { "System.IntPtr", "::BNM::Types::nint" },
            { "System.UIntPtr", "::BNM::Types::nuint" },
            { "System.Object", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.StringComparison", "int" },
            { "System.Action", "::BNM::Structures::Mono::Action<>*" },
            { "System.Collections.IEnumerator", "::BNM::Coroutine::IEnumerator*" },
            { "System.Collections.IEnumerable", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.ICollection", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IList", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IDictionary", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IComparer", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IEqualityComparer", "::BNM::IL2CPP::Il2CppObject*" },
            { "UnityEngine.Vector2", "::BNM::Structures::Unity::Vector2" },
            { "UnityEngine.Vector3", "::BNM::Structures::Unity::Vector3" },
            { "UnityEngine.Vector4", "::BNM::Structures::Unity::Vector4" },
            { "UnityEngine.Quaternion", "::BNM::Structures::Unity::Quaternion" },
            { "UnityEngine.Matrix4x4", "::BNM::Structures::Unity::Matrix4x4" },
            { "UnityEngine.Color", "::BNM::Structures::Unity::Color" },
            { "UnityEngine.Rect", "::BNM::Structures::Unity::Rect" },
            { "UnityEngine.Ray", "::BNM::Structures::Unity::Ray" },
            { "UnityEngine.RaycastHit", "::BNM::Structures::Unity::RaycastHit" },
        };

        public static readonly Dictionary<string, string> BnmResolveTypeMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "UnityEngine.Transform", "::Transform*" },
            { "UnityEngine.GameObject", "::GameObject*" },
            { "UnityEngine.Component", "::Component*" },
            { "UnityEngine.Collider", "::Collider*" },
            { "UnityEngine.Rigidbody", "::Rigidbody*" },
            { "UnityEngine.Animator", "::Animator*" },
            { "UnityEngine.Camera", "::Camera*" },
            { "UnityEngine.Canvas", "::Canvas*" },
            { "UnityEngine.RectTransform", "::RectTransform*" },
            { "UnityEngine.UI.Text", "::Text*" },
            { "UnityEngine.Behaviour", "::Behaviour*" },
            { "UnityEngine.UI.CanvasScaler", "::CanvasScaler*" },
            { "UnityEngine.EventSystems.UIBehavior", "::UIBehavior*" },
            { "UnityEngine.EventSystems.BaseRaycaster", "::BaseRaycaster*" },
            { "UnityEngine.UI.GraphicRaycaster", "::GraphicRaycaster*" },
            { "UnityEngine.Shader", "::Shader*" },
            { "UnityEngine.Material", "::Material*" },
            { "UnityEngine.Renderer", "::Renderer*" },
            { "UnityEngine.SkinnedMeshRenderer", "::SkinnedMeshRenderer*" },
            { "UnityEngine.UI.Graphic", "::Graphic*" },
            { "UnityEngine.UI.MaskableGraphic", "::MaskableGraphic*" },
            { "UnityEngine.UI.Font", "::Font*" },
            { "UnityEngine.LineRenderer", "::LineRenderer*" },
            { "UnityEngine.Time", "::Time*" },
            { "UnityEngine.SphereCollider", "::SphereCollider*" },
            { "UnityEngine.BoxCollider", "::BoxCollider*" },
            { "UnityEngine.MeshRenderer", "::MeshRenderer*" },
            { "UnityEngine.Resources", "::Resources*" },
            { "UnityEngine.AssetBundle", "::AssetBundle*" },
            { "UnityEngine.Physics", "::Physics*" },
            { "UnityEngine.LightmapData", "::LightmapData*" },
            { "UnityEngine.LightmapSettings", "::LightmapSettings*" },
            { "UnityEngine.Texture2D", "::Texture2D*" },
            { "UnityEngine.Gradient", "::Gradient*" },
            { "UnityEngine.Skybox", "::Skybox*" },
            { "UnityEngine.Sprite", "::Sprite*" },
            { "UnityEngine.QualitySettings", "::QualitySettings*" },
            { "UnityEngine.ParticleSystem", "::ParticleSystem*" },
            { "UnityEngine.ParticleSystem.EmissionModule", "::EmissionModule*" },
            { "UnityEngine.Light", "::Light*" },
            { "UnityEngine.AudioClip", "::AudioClip*" },
            { "UnityEngine.AudioSource", "::AudioSource*" },
            { "UnityEngine.LODGroup", "::LODGroup*" },
            { "UnityEngine.MonoBehaviour", "::MonoBehaviour*" },
            { "UnityEngine.Application", "::Application*" },
            { "UnityEngine.Networking.UnityWebRequest", "::UnityWebRequest*" },
            { "UnityEngine.Networking.DownloadHandler.DownloadHandlerTexture", "::DownloadHandlerTexture*" },
            { "UnityEngine.GL", "::GL*" },
            { "TMPro.TextMeshPro", "::TextMeshPro*" },
            { "TMPro.TMP_Text", "::TMP_Text*" },
        };

        private static readonly HashSet<string> SystemValueTypeBlacklist = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.DateTime",
            "System.TimeSpan",
            "System.Guid",
            "System.Decimal",
            "System.DateTimeOffset",
            "System.DateOnly",
            "System.TimeOnly",
            "System.Numerics.Vector2",
            "System.Numerics.Vector3",
            "System.Numerics.Vector4",
            "System.Numerics.Quaternion",
            "System.Numerics.Matrix4x4",
        };

        private static readonly HashSet<string> CppKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "alignas",
            "alignof",
            "and",
            "and_eq",
            "asm",
            "auto",
            "bitand",
            "bitor",
            "bool",
            "break",
            "case",
            "catch",
            "char",
            "char8_t",
            "char16_t",
            "char32_t",
            "class",
            "compl",
            "concept",
            "const",
            "consteval",
            "constexpr",
            "constinit",
            "continue",
            "co_await",
            "co_return",
            "co_yield",
            "decltype",
            "default",
            "delete",
            "do",
            "double",
            "dynamic_cast",
            "else",
            "enum",
            "explicit",
            "export",
            "extern",
            "false",
            "float",
            "for",
            "friend",
            "goto",
            "if",
            "inline",
            "int",
            "long",
            "mutable",
            "namespace",
            "new",
            "noexcept",
            "not",
            "not_eq",
            "nullptr",
            "operator",
            "or",
            "or_eq",
            "private",
            "protected",
            "public",
            "register",
            "reinterpret_cast",
            "requires",
            "return",
            "short",
            "signed",
            "sizeof",
            "static",
            "static_assert",
            "static_cast",
            "struct",
            "switch",
            "template",
            "this",
            "thread_local",
            "throw",
            "true",
            "try",
            "typedef",
            "typeid",
            "typename",
            "union",
            "unsigned",
            "using",
            "virtual",
            "void",
            "volatile",
            "wchar_t",
            "while",
            "xor",
            "xor_eq",
        };

        private static readonly HashSet<string> SystemNamespacePrefixes = new HashSet<string>(StringComparer.Ordinal) { "System", "Microsoft", "Mono", "Internal", "Interop", "JetBrains" };

        private static readonly HashSet<string> UnityNamespacePrefixes = new HashSet<string>(StringComparer.Ordinal) { "Unity", "UnityEngine", "UnityEditor", "TMPro" };

        public static string CleanTypeName(string typeName) => typeName;

        public static string GetNamespace(TypeReference type)
        {
            if (type.IsNested)
                return GetNamespace(type.DeclaringType);
            string ns = type.Namespace;
            return string.IsNullOrEmpty(ns) ? "GlobalNamespace" : ns;
        }

        public static bool IsSystemNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns))
                return false;
            foreach (var prefix in SystemNamespacePrefixes)
                if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static bool IsUnityNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns))
                return false;
            foreach (var prefix in UnityNamespacePrefixes)
                if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static string FixNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns) || ns == "GlobalNamespace")
                return "GlobalNamespace";
            var sb = new StringBuilder(ns.Length + 4);
            bool prevUnderscore = false;
            foreach (char c in ns)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    prevUnderscore = false;
                }
                else
                {
                    if (!prevUnderscore)
                        sb.Append('_');
                    prevUnderscore = true;
                }
            }
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        public static string GetFullCppPath(TypeDefinition type) => "::" + FixNamespace(GetNamespace(type)) + "::" + FormatTypeNameForStruct(type);

        public static string FormatTypeNameForStruct(TypeDefinition type)
        {
            if (type.IsNested)
                return FormatTypeNameForStruct(type.DeclaringType) + "_" + FormatInvalidName(CleanTypeName(type.Name));
            return FormatInvalidName(CleanTypeName(type.Name));
        }

        public static string FormatInvalidName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";
            var sb = new StringBuilder(name.Length + 2);
            foreach (char c in name.Trim())
            {
                switch (c)
                {
                    case '<':
                    case '>':
                    case '|':
                    case '-':
                    case '`':
                    case '=':
                    case '@':
                        sb.Append('$');
                        break;
                    case '.':
                    case ':':
                    case ' ':
                        sb.Append('_');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            string result = sb.ToString();
            if (IsKeyword(result))
                result = "$" + result;
            return result;
        }

        public static bool IsKeyword(string str) => CppKeywords.Contains(str);

        public static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var parts = input.Split('_');
            var sb = new StringBuilder(input.Length);
            foreach (var part in parts)
            {
                if (part.Length == 0)
                    continue;
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    sb.Append(part, 1, part.Length - 1);
            }
            return sb.ToString();
        }

        public static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            string p = ToPascalCase(input);
            return p.Length > 0 ? char.ToLowerInvariant(p[0]) + p.Substring(1) : p;
        }

        public static string[] MakeValidParams(string[] paramNames)
        {
            var results = new string[paramNames.Length];
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < paramNames.Length; i++)
            {
                string p = string.IsNullOrEmpty(paramNames[i]) ? $"param{i}" : paramNames[i];
                if (IsKeyword(p))
                    p = "_" + p;
                if (seen.TryGetValue(p, out int count))
                {
                    seen[p] = count + 1;
                    results[i] = $"{p}_{count + 1}";
                }
                else
                {
                    seen[p] = 0;
                    results[i] = p;
                }
            }
            return results;
        }

        public static string GetEnumUnderlyingType(TypeDefinition enumType)
        {
            var field = enumType.Fields.FirstOrDefault(f => f.Name == "value__");
            if (field == null)
                return "int";
            string t = GetCppType(field.FieldType);
            return (t == "void*" || t.Contains("void")) ? "int" : t;
        }

        public static string GetClassGetter(TypeDefinition type)
        {
            var tree = new List<TypeDefinition>();
            var cur = type;
            while (cur != null)
            {
                tree.Add(cur);
                cur = cur.DeclaringType;
            }
            tree.Reverse();
            var sb = new StringBuilder();
            var root = tree[0];
            sb.Append($"::BNM::Class(\"{root.Namespace}\", \"{root.Name}\")");
            for (int i = 1; i < tree.Count; i++)
                sb.Append($".GetInnerClass(\"{tree[i].Name}\")");
            return sb.ToString();
        }

        public static string GetRelativeIncludePath(string fromNs, string toNs, string typeName)
        {
            string from = FixNamespace(fromNs);
            string to = FixNamespace(toNs);
            return from == to ? $"{typeName}.hpp" : $"../{to}/{typeName}.hpp";
        }

        public static bool ShouldAddDependency(TypeReference type, TypeDefinition context, bool ignoreClassCheck = false)
        {
            if (type == null)
                return false;
            var resolved = type as TypeDefinition ?? type.Resolve();
            if (resolved == null)
                return false;
            string fn = resolved.FullName;
            if (fn == context?.FullName)
                return false;
            if (PrimitiveTypeMappings.ContainsKey(fn) || BnmResolveTypeMappings.ContainsKey(fn))
                return false;
            if (IsSystemNamespace(resolved.Namespace) || IsUnityNamespace(resolved.Namespace))
                return false;
            if (!Program.DefinedTypes.Contains(fn))
                return false;
            if (ignoreClassCheck)
                return true;
            return resolved.IsEnum || resolved.IsValueType;
        }

        public static string GetCppType(TypeReference typeRef, TypeDefinition context = null, HashSet<TypeDefinition> deps = null)
        {
            if (typeRef == null)
                return "void*";
            if (typeRef.IsByReference)
                return GetCppType(typeRef.GetElementType(), context, deps) + "&";
            if (typeRef.IsPointer)
                return GetCppType(typeRef.GetElementType(), context, deps) + "*";
            if (typeRef.IsArray)
                return $"::BNM::Structures::Mono::Array<{GetCppType(typeRef.GetElementType(), context, deps)}>*";
            if (typeRef.IsGenericParameter)
                return typeRef.Name;

            string fullName = typeRef.FullName;
            if (PrimitiveTypeMappings.TryGetValue(fullName, out var prim))
                return prim;
            if (SystemValueTypeBlacklist.Contains(fullName))
                return "void*";
            if (Config.UseBNMResolve && BnmResolveTypeMappings.TryGetValue(fullName, out var bnm))
                return bnm;

            if (typeRef is GenericInstanceType git && TryMapGeneric(git, context, deps, out var genResult))
                return genResult;

            var resolved = typeRef as TypeDefinition ?? typeRef.Resolve();
            if (resolved == null)
                return typeRef.IsValueType ? "void*" : "::BNM::IL2CPP::Il2CppObject*";
            if (IsSystemNamespace(resolved.Namespace))
                return resolved.IsValueType ? "void*" : "::BNM::IL2CPP::Il2CppObject*";
            if (IsUnityNamespace(resolved.Namespace))
                return resolved.IsValueType ? "void*" : "::BNM::IL2CPP::Il2CppObject*";
            if (resolved.IsInterface)
                return "void*";
            if (!Program.DefinedTypes.Contains(resolved.FullName))
                return resolved.IsValueType ? "void*" : "::BNM::IL2CPP::Il2CppObject*";

            if (deps != null && ShouldAddDependency(resolved, context))
                deps.Add(resolved);

            string path = GetFullCppPath(resolved);
            return (resolved.IsEnum || resolved.IsValueType) ? path : path + "*";
        }

        private static bool TryMapGeneric(GenericInstanceType git, TypeDefinition context, HashSet<TypeDefinition> deps, out string result)
        {
            string baseFull = git.ElementType.FullName;
            int lt = baseFull.IndexOf('<');
            if (lt >= 0)
                baseFull = baseFull.Substring(0, lt);

            string args = string.Join(", ", git.GenericArguments.Select(a => GetCppType(a, context, deps)));

            switch (baseFull)
            {
                case "System.Collections.Generic.List`1":
                    result = $"::BNM::Structures::Mono::List<{args}>*";
                    return true;
                case "System.Collections.Generic.Dictionary`2":
                    result = $"::BNM::Structures::Mono::Dictionary<{args}>*";
                    return true;
            }
            if (baseFull.StartsWith("System.Action`", StringComparison.Ordinal))
            {
                result = $"::BNM::Structures::Mono::Action<{args}>*";
                return true;
            }
            if (baseFull.StartsWith("System.Func`", StringComparison.Ordinal))
            {
                result = $"::BNM::Structures::Mono::Func<{args}>*";
                return true;
            }

            var resBase = git.ElementType.Resolve();
            if (resBase != null && Program.DefinedTypes.Contains(resBase.FullName))
            {
                if (deps != null && ShouldAddDependency(resBase, context))
                    deps.Add(resBase);
                result = $"{GetFullCppPath(resBase)}<{args}>*";
                return true;
            }

            result = "void*";
            return true;
        }
    }
}
