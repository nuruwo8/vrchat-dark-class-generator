#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Nuruwo.Tool
{
    public class DarkClassGeneratorEditorWindow : EditorWindow
    {
        /*------------------------------private values----------------------------------*/
        private bool _foldIsOpen = false;

        private TextAsset _textAsset;
        private string _prevScriptName = string.Empty;
        private ReorderableList _reorderableList;
        private string _className = string.Empty;
        private string _nameSpace = string.Empty;
        private List<string> _fieldList = new List<string>() { "int value" };
        private string _result = string.Empty;
        private string _generatedCode = string.Empty;
        private Vector2 _scrollPosition = Vector2.zero; //for textArea scroll
        /*------------------------------Initialize------------------------------*/
        [MenuItem("Tools/Nuruwo/DarkClassGenerator", false, 1)]
        public static void Open()
        {
            var window = GetWindow<DarkClassGeneratorEditorWindow>();
            window.titleContent = new GUIContent("Dark class generator");
        }

        private void OnEnable()
        {
            UpdateReorderableList(_fieldList);
        }

        /*------------------------------UI Look---------------------------------*/

        private void OnGUI()
        {
            //load from script
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _foldIsOpen = EditorGUILayout.Foldout(_foldIsOpen, "Load parameters from script");
            if (_foldIsOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.Space(10);
                EditorGUILayout.BeginHorizontal();
                _textAsset = (TextAsset)EditorGUILayout.ObjectField("Script to load", _textAsset, typeof(TextAsset), false);
                var scriptName = _textAsset != null ? _textAsset.name : string.Empty;
                if (!string.IsNullOrEmpty(scriptName) && _prevScriptName != scriptName)
                {
                    _prevScriptName = scriptName;   //script is changed
                    LoadScript(_textAsset.text);
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scriptName)))
                {
                    if (GUILayout.Button("Reload script"))
                    {
                        LoadScript(_textAsset.text);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Class parameters", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            //namespace
            EditorGUILayout.Space(10);
            _nameSpace = EditorGUILayout.TextField("namespace (optional)", _nameSpace);
            EditorGUILayout.Space(10);

            //Class name
            EditorGUILayout.Space(10);
            _className = EditorGUILayout.TextField("Class name", _className);
            EditorGUILayout.Space(10);

            //Augment list
            _reorderableList.DoLayoutList();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            //Generate button
            var canGenerate = !string.IsNullOrEmpty(_className) && _fieldList.Count > 0 && !string.IsNullOrEmpty(_fieldList[0]);
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                //Generate 
                if (GUILayout.Button("Generate DarkClass"))
                {
                    _generatedCode = GenerateDarkClass();
                    //clip board
                    EditorGUIUtility.systemCopyBuffer = _generatedCode;
                    _result = "Code is generated and Copied to clipboard.";
                }
            }
            EditorGUILayout.Space(10);

            //textArea
            GUILayout.Label(_result);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            var textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true
            };
            GUILayout.TextArea(_generatedCode, textAreaStyle);
            EditorGUILayout.EndScrollView();
        }

        /*------------------------------Generate DarkClass-----------------------------*/

        private string GenerateDarkClass()
        {
            var sb = new StringBuilder();
            var pClassName = ToPascal(_className);
            var enumName = pClassName + "Field";

            sb.Append(GenerateHeaderWithNameSpace(_nameSpace));
            var nameSpaceIsExist = !string.IsNullOrEmpty(_nameSpace);
            var nst = nameSpaceIsExist ? 1 : 0; //namespace tab number

            //enum
            sb.Append(AddTabLines("// Enum for assigning index of field objects\r\n", nst));
            var enumString = GenerateEnum(enumName);
            sb.Append(AddTabLines(enumString, nst));
            sb.AppendLine();

            //main class
            var classHeaderString = GenerateClassHeader(pClassName);
            sb.Append(AddTabLines(classHeaderString, nst));
            sb.Append(AddTabLines("// Constructor\r\n", nst + 1));
            var mainClassString = GenerateConstructor(pClassName, enumName, _fieldList);
            sb.Append(AddTabLines(mainClassString, nst + 1));
            sb.Append(GenerateClassFooter(nameSpaceIsExist));
            sb.AppendLine();

            //extension class
            var extensionClassHeaderString = GenerateExtensionClassHeader(pClassName);
            sb.Append(AddTabLines(extensionClassHeaderString, nst));
            sb.Append(AddTabLines("// Get methods\r\n", nst + 1));
            var getMethodsString = GenerateGetMethods(pClassName, enumName, _fieldList);
            sb.Append(AddTabLines(getMethodsString, nst + 1));
            sb.AppendLine();
            sb.Append(AddTabLines("// Set methods\r\n", nst + 1));
            var setMethodsString = GenerateSetMethods(pClassName, enumName, _fieldList);
            sb.Append(AddTabLines(setMethodsString, nst + 1));
            sb.Append(GenerateFooter(nameSpaceIsExist));

            //result
            return sb.ToString();
        }
        private string GenerateClassFooter(bool nameSpaceIsExist)
        {
            return nameSpaceIsExist ? "\t}\r\n" : "}\r\n";
        }

        private string AddTabLines(string srcText, int tabNum)
        {
            var sb = new StringBuilder();
            var texts = srcText.Split("\r\n");
            for (int i = 0; i < texts.Length - 1; i++)  //last line is empty
            {
                var tabText = String.Copy(texts[i]);
                for (int j = 0; j < tabNum; j++)
                {
                    tabText = tabText.Insert(0, "\t");
                }
                sb.AppendLine(tabText);
            }
            return sb.ToString();
        }
        private string GenerateHeaderWithNameSpace(string nameSpace)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"using UdonSharp;");
            sb.AppendLine($"using UnityEngine;");
            sb.AppendLine("");
            if (!string.IsNullOrEmpty(nameSpace))
            {
                sb.AppendLine($"namespace {nameSpace}");
                sb.AppendLine($"{{");
            }
            return sb.ToString();
        }

        private string GenerateEnum(string enumName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"enum {enumName}");
            sb.AppendLine($"{{");
            foreach (var argument in _fieldList)
            {
                var args = argument.Split(' ');
                if (args.Length != 2) { continue; }
                var argumentName = args[1];
                sb.AppendLine($"\t{ToPascal(argumentName)},");
            }
            sb.AppendLine($"");
            sb.AppendLine($"\tCount");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private string GenerateClassHeader(string pClassName)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"[AddComponentMenu(\"\")]");
            sb.AppendLine($"public class {pClassName} : UdonSharpBehaviour");
            sb.AppendLine($"{{");
            return sb.ToString();
        }

        private string GenerateConstructor(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.Append($"public static {pClassName} New(");
            foreach (var argument in fieldList)
            {
                var args = argument.Split(' ');
                if (args.Length != 2) { continue; }
                var argumentType = args[0];
                var argumentName = args[1];
                sb.Append($"{argumentType} {argumentName}, ");
            }
            sb.Remove(sb.Length - 2, 2);    //remove last comma and space
            sb.AppendLine($")");
            sb.AppendLine($"{{");
            sb.AppendLine($"\tvar buff = new object[(int){enumName}.Count];");
            sb.AppendLine();

            var buffBase = $"\tbuff[(int)";
            foreach (var argument in fieldList)
            {
                var args = argument.Split(' ');
                if (args.Length != 2) { continue; }
                var argumentName = args[1];
                var pArgumentName = ToPascal(argumentName);
                sb.AppendLine($"{buffBase}{enumName}.{pArgumentName}] = {argumentName};");
            }

            sb.AppendLine();
            sb.AppendLine($"\treturn ({pClassName})(object)buff;");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private string GenerateExtensionClassHeader(string pClassName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public static class {pClassName}Ext");
            sb.AppendLine($"{{");
            return sb.ToString();
        }

        private string GenerateGetMethods(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            foreach (var argument in fieldList)
            {
                var args = argument.Split(' ');
                if (args.Length != 2) { continue; }
                var argumentType = args[0];
                var pArgumentName = ToPascal(args[1]);

                sb.AppendLine($"public static {argumentType} {pArgumentName}(this {pClassName} instance)");
                sb.AppendLine($"\t=> ({argumentType})((object[])(object)instance)[(int){enumName}.{pArgumentName}];");
            }
            return sb.ToString();
        }

        private string GenerateSetMethods(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            foreach (var argument in fieldList)
            {
                var args = argument.Split(' ');
                if (args.Length != 2) { continue; }
                var argumentType = args[0];
                var pArgumentName = ToPascal(args[1]);

                sb.AppendLine($"public static void {pArgumentName}(this {pClassName} instance, {argumentType} arg)");
                sb.AppendLine($"\t=> ((object[])(object)instance)[(int){enumName}.{pArgumentName}] = arg;");
            }
            return sb.ToString();
        }

        private string GenerateFooter(bool nameSpaceIsExist)
        {
            var sb = new StringBuilder();
            if (nameSpaceIsExist)
            {
                sb.AppendLine("\t}");
            }
            sb.Append("}");
            return sb.ToString();
        }

        /*------------------------------Load script---------------------------------*/
        private void LoadScript(string text)
        {
            Debug.Log("LoadScript: " + text);
            var rows = text.Replace("\r\n", "\n").Split(new[] { '\n', '\r' });
            text = text.Replace("\r\n", "\n").Replace("\n", " "); //remove line break and join with space

            //namespace
            var namespaceString = ExtractNameSpace(text);
            if (!string.IsNullOrEmpty(namespaceString)) { _nameSpace = namespaceString; }

            //className
            var classNameString = ExtractClassName(text);
            if (!string.IsNullOrEmpty(classNameString)) { _className = classNameString; }

            //Fields
            if (!string.IsNullOrEmpty(classNameString))
            {
                var fieldStrings = ExtractFields(text, classNameString);
                if (fieldStrings.Count > 0) { UpdateReorderableList(fieldStrings); }
            }
        }

        private List<string> ExtractFields(string text, string className)
        {
            var fields = new List<string>();

            var matches = Regex.Matches(text, @"\s*public\s+static\s+" + className + @"\s+New\s*\([^\)]+\)");
            if (matches.Count() <= 0) { return fields; }
            var match = matches[0].Value.Trim();
            if (!string.IsNullOrEmpty(match))
            {
                var startIndex = match.IndexOf("(");
                var endIndex = match.IndexOf(")");
                var src = match.Substring(startIndex + 1, endIndex - startIndex - 1);
                var words = Regex.Split(src, @"\s*,\s*");
                if (words.Length > 0)
                {
                    foreach (var word in words)
                    {
                        Debug.Log(word);
                    }
                    fields.AddRange(words);
                }
            }

            return fields;
        }

        private string ExtractClassName(string text)
        {
            var matches = Regex.Matches(text, @"\s*public\s+class\s+([a-zA-Z0-9_]+)\s+:");
            if (matches.Count() <= 0) { return string.Empty; }
            var match = matches[0].Value.Trim();
            if (!string.IsNullOrEmpty(match))
            {
                var words = Regex.Split(match, @"\s+");
                var classIndex = Array.IndexOf(words, "class");
                if (words.Length >= classIndex + 1)
                {
                    return words[classIndex + 1];
                }
            }
            return string.Empty;
        }

        private string ExtractNameSpace(string text)
        {
            var matches = Regex.Matches(text, @"\s*namespace\s+([a-zA-Z0-9_.]+)\s");
            if (matches.Count() <= 0) { return string.Empty; }
            var match = matches[0].Value.Trim();
            if (!string.IsNullOrEmpty(match))
            {
                var words = Regex.Split(match, @"\s+");
                if (words.Length >= 2)
                {
                    return words[1];
                }
            }
            return string.Empty;
        }

        private void UpdateReorderableList(List<string> list)
        {
            _fieldList = list;
            _reorderableList = new ReorderableList(
              elements: _fieldList,
              elementType: typeof(string),
              draggable: true,
              displayHeader: true,
              displayAddButton: true,
              displayRemoveButton: true
            );
            _reorderableList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Field List");
            _reorderableList.elementHeightCallback = index => 20;
            _reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                _fieldList[index] = EditorGUI.TextField(rect, "  Field " + index, _fieldList[index]);
            };
        }

        /*------------------------------Utility----------------------------------*/
        private static string ToPascal(string text)
        {
            return Regex.Replace(text.Replace("_", " "), @"\b[a-z]", match => match.Value.ToUpper()).Replace(" ", "");
        }
    }
}
#endif