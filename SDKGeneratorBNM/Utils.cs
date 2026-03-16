using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    public static class Utils
    {
        public static readonly HashSet<string> ReservedTypeNames = new HashSet<string>();

        private static readonly Dictionary<string, string> PrimitiveTypeMappings = new Dictionary<string, string>
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
            { "System.Collections.IEnumerator", "::BNM::Coroutine::IEnumerator*" },
            { "System.StringComparison", "int" },
            { "System.Object", "void*" },
            { "UnityEngine.Vector2", "::BNM::Structures::Unity::Vector2" },
            { "UnityEngine.Vector3", "::BNM::Structures::Unity::Vector3" },
            { "UnityEngine.Vector4", "::BNM::Structures::Unity::Vector4" },
            { "UnityEngine.Quaternion", "::BNM::Structures::Unity::Quaternion" },
            { "UnityEngine.Matrix4x4", "::BNM::Structures::Unity::Matrix4x4" },
            { "UnityEngine.Color", "::BNM::Structures::Unity::Color" },
            { "UnityEngine.Rect", "::BNM::Structures::Unity::Rect" },
            { "UnityEngine.Ray", "::BNM::Structures::Unity::Ray" },
            { "UnityEngine.RaycastHit", "::BNM::Structures::Unity::RaycastHit" },
            { "System.Action", "::BNM::Structures::Mono::Action<>*" },
            { "System.Collections.IEnumerable", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.ICollection", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IList", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IDictionary", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IComparer", "::BNM::IL2CPP::Il2CppObject*" },
            { "System.Collections.IEqualityComparer", "::BNM::IL2CPP::Il2CppObject*" },
        };

        public static readonly Dictionary<string, string> BnmResolveTypeMappings = new Dictionary<string, string>
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

        private static readonly HashSet<string> CppKeywords = new HashSet<string>
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
            "class",
            "compl",
            "concept",
            "const",
            "consteval",
            "constexpr",
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

        public static string CleanTypeName(string typeName) => typeName;

        public static string GetNamespace(TypeReference type)
        {
            if (type.IsNested)
                return GetNamespace(type.DeclaringType);
            string ns = type.Namespace;
            return (string.IsNullOrEmpty(ns) || ns == "GlobalNamespace") ? "GlobalNamespace" : ns;
        }

        public static string FixNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns) || ns == "GlobalNamespace")
                return "GlobalNamespace";
            string fixedNs = ns.Replace(".", "_");
            foreach (char c in Path.GetInvalidFileNameChars())
                fixedNs = fixedNs.Replace(c, '_');
            var sb = new StringBuilder(fixedNs.Length);
            foreach (char c in fixedNs)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        public static string GetFullCppPath(TypeDefinition type)
        {
            return "::" + FixNamespace(GetNamespace(type)) + "::" + FormatTypeNameForStruct(type);
        }

        public static bool ShouldAddDependency(TypeReference type, TypeDefinition context, bool ignoreClassCheck = false)
        {
            if (type == null)
                return false;
            var resolved = type.Resolve();
            if (resolved == null)
                return false;
            string fullName = resolved.FullName;
            if (fullName == context.FullName)
                return false;
            if (PrimitiveTypeMappings.ContainsKey(fullName) || BnmResolveTypeMappings.ContainsKey(fullName))
                return false;
            if (resolved.Namespace == "System" || resolved.Namespace.StartsWith("System."))
                return false;
            if (resolved.Namespace.StartsWith("UnityEngine"))
                return false;
            if (!Program.DefinedTypes.Contains(fullName))
                return false;
            if (ignoreClassCheck)
                return true;
            return resolved.IsEnum || resolved.IsValueType;
        }

        public static string GetCppType(TypeReference typeRef, TypeDefinition context = null, HashSet<TypeDefinition> deps = null)
        {
            if (typeRef.IsByReference)
                return GetCppType(typeRef.GetElementType(), context, deps) + "&";
            if (typeRef.IsPointer)
                return GetCppType(typeRef.GetElementType(), context, deps) + "*";
            if (typeRef.IsArray)
                return $"::BNM::Structures::Mono::Array<{GetCppType(typeRef.GetElementType(), context, deps)}>*";
            if (typeRef.IsGenericParameter)
                return typeRef.Name;

            try
            {
                string fullName = typeRef.FullName;
                if (PrimitiveTypeMappings.TryGetValue(fullName, out var mappedType))
                    return mappedType;
                if (Config.UseBNMResolve && BnmResolveTypeMappings.TryGetValue(fullName, out var bnmType))
                    return bnmType;

                if (typeRef is GenericInstanceType git)
                {
                    string baseFullName = git.ElementType.FullName.Split('<')[0];
                    string args = string.Join(", ", git.GenericArguments.Select(a => GetCppType(a, context, deps)));
                    if (baseFullName == "System.Collections.Generic.List`1")
                        return $"::BNM::Structures::Mono::List<{args}>*";
                    if (baseFullName == "System.Collections.Generic.Dictionary`2")
                        return $"::BNM::Structures::Mono::Dictionary<{args}>*";
                    if (baseFullName.StartsWith("System.Action`"))
                        return $"::BNM::Structures::Mono::Action<{args}>*";
                    if (baseFullName.StartsWith("System.Func`"))
                        return $"::BNM::Structures::Mono::Func<{args}>*";

                    var resBase = git.ElementType.Resolve();
                    if (resBase != null)
                    {
                        if (Program.DefinedTypes.Contains(resBase.FullName))
                        {
                            if (deps != null && ShouldAddDependency(resBase, context))
                                deps.Add(resBase);
                            return $"{GetFullCppPath(resBase)}<{args}>*";
                        }
                        return "void*";
                    }
                    return "void*";
                }

                var resolved = typeRef.Resolve();
                if (resolved == null)
                    return "::BNM::IL2CPP::Il2CppObject*";

                if (resolved.Namespace.StartsWith("UnityEngine"))
                    return resolved.IsValueType ? "void*" : "::BNM::IL2CPP::Il2CppObject*";

                if (resolved.IsInterface)
                    return "void*";

                if (!Program.DefinedTypes.Contains(resolved.FullName))
                    return resolved.IsValueType ? "void*" : "::BNM::IL2CPP::Il2CppObject*";

                if (deps != null && ShouldAddDependency(resolved, context))
                    deps.Add(resolved);

                string cppPath = GetFullCppPath(resolved);
                if (resolved.IsEnum || resolved.IsValueType)
                    return cppPath;
                return cppPath + "*";
            }
            catch
            {
                return "void*";
            }
        }

        public static string GetClassGetter(TypeDefinition type, string varName = "")
        {
            var defTree = new List<TypeDefinition>();
            var current = type;
            while (current != null)
            {
                defTree.Add(current);
                current = current.DeclaringType;
            }
            defTree.Reverse();
            var builder = new StringBuilder();
            var firstType = defTree[0];
            if (string.IsNullOrEmpty(varName))
                builder.Append($"::BNM::Class(\"{firstType.Namespace}\", \"{firstType.Name}\")");
            else
                builder.Append($"::BNM::Class {varName} = ::BNM::Class(\"{firstType.Namespace}\", \"{firstType.Name}\")");
            for (int i = 1; i < defTree.Count; i++)
                builder.Append($".GetInnerClass(\"{defTree[i].Name}\")");
            return builder.ToString();
        }

        public static string GetRelativeIncludePath(string from, string to, string typeName)
        {
            if (from == to)
                return $"{typeName}.hpp";
            string fromDir = FixNamespace(from);
            string toDir = FixNamespace(to);
            if (fromDir == toDir)
                return $"{typeName}.hpp";
            return $"../{toDir}/{typeName}.hpp";
        }

        public static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var parts = input.Split('_');
            var result = new StringBuilder();
            foreach (var part in parts)
                if (!string.IsNullOrEmpty(part))
                {
                    result.Append(char.ToUpper(part[0]));
                    if (part.Length > 1)
                        result.Append(part.Substring(1));
                }
            return result.ToString();
        }

        public static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            string pascalCase = ToPascalCase(input);
            return pascalCase.Length > 0 ? char.ToLower(pascalCase[0]) + pascalCase.Substring(1) : pascalCase;
        }

        public static bool IsKeyword(string str) => CppKeywords.Contains(str);

        public static string[] MakeValidParams(string[] paramNames)
        {
            var results = new List<string>();
            var seen = new Dictionary<string, int>();
            foreach (var param in paramNames)
            {
                string cparam = param;
                if (IsKeyword(cparam))
                    cparam = $"_{cparam}";
                if (seen.ContainsKey(cparam))
                {
                    seen[cparam]++;
                    cparam = $"{cparam}_{seen[cparam]}";
                }
                else
                    seen[cparam] = 0;
                results.Add(cparam);
            }
            return results.ToArray();
        }

        public static string FormatInvalidName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";
            string str = name.Trim().Replace("<", "$").Replace(">", "$").Replace("|", "$").Replace("-", "$").Replace("`", "$").Replace("=", "$").Replace("@", "$").Replace(".", "_").Replace(":", "_").Replace(" ", "_");
            if (char.IsDigit(str[0]))
                str = "_" + str;
            if (IsKeyword(str))
                str = "$" + str;
            return str;
        }

        public static string FormatTypeNameForStruct(TypeDefinition type)
        {
            if (type.IsNested)
                return FormatTypeNameForStruct(type.DeclaringType) + "_" + FormatInvalidName(CleanTypeName(type.Name));
            return FormatInvalidName(CleanTypeName(type.Name));
        }

        public static string GetEnumUnderlyingType(TypeDefinition enumType)
        {
            var field = enumType.Fields.FirstOrDefault(f => f.Name == "value__");
            if (field == null)
                return "int";
            string cppType = GetCppType(field.FieldType);
            return (cppType == "void*" || cppType.Contains("void")) ? "int" : cppType;
        }
    }
}
