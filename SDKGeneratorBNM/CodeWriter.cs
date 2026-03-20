using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    public sealed class CodeWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private int _indentLevel;
        private const string IndentStr = "    ";

        public HashSet<TypeDefinition> Imports = new HashSet<TypeDefinition>(new TypeDefComparer());
        public string CurrentNamespace = "";

        public void Indent() => _indentLevel++;

        public void Unindent()
        {
            if (_indentLevel > 0)
                _indentLevel--;
        }

        public void Line(string text = "")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _sb.AppendLine();
                return;
            }
            for (int i = 0; i < _indentLevel; i++)
                _sb.Append(IndentStr);
            _sb.AppendLine(text);
        }

        public void Raw(string text) => _sb.Append(text);

        public void StartNamespace(string ns)
        {
            CurrentNamespace = string.IsNullOrEmpty(ns) ? "GlobalNamespace" : ns;
            Line($"namespace {Utils.FixNamespace(CurrentNamespace)} {{");
            Indent();
        }

        public void EndNamespace()
        {
            Unindent();
            Line("}");
        }

        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var header = new StringBuilder(512);
            header.AppendLine("#pragma once");
            header.AppendLine("#include <BNMIncludes.hpp>");
            if (Config.UseBNMResolve)
                header.AppendLine("#include <BNMResolve.hpp>");

            bool isForwardDec = path.EndsWith("ForwardDeclarations.hpp", StringComparison.Ordinal);
            if (!isForwardDec)
                header.AppendLine("#include \"../ForwardDeclarations.hpp\"");

            var sortedImports = Imports.Where(i => i != null).Select(i => new { Ns = Utils.GetNamespace(i), Name = Utils.FormatTypeNameForStruct(i) }).GroupBy(i => i.Ns + "." + i.Name).Select(g => g.First()).OrderBy(i => i.Ns).ThenBy(i => i.Name);

            string currentFileName = Path.GetFileName(path);
            foreach (var imp in sortedImports)
            {
                string impFile = imp.Name + ".hpp";
                if (imp.Ns == CurrentNamespace && impFile == currentFileName)
                    continue;
                string rel = Utils.GetRelativeIncludePath(CurrentNamespace, imp.Ns, imp.Name);
                header.AppendLine($"#include \"{rel}\"");
            }
            header.AppendLine();

            string content = header.ToString() + _sb.ToString().Replace("StringComparison", "int");
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content, new UTF8Encoding(false, false));
        }

        public override string ToString() => _sb.ToString();
    }
}
