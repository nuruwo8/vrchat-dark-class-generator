#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        private bool _loadFoldIsOpen = false;
        private bool _optionFoldIsOpen = false;

        private bool _generateSetMethod = true;
        private bool _jsonDeserializeMode = false;

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
            _loadFoldIsOpen = EditorGUILayout.Foldout(_loadFoldIsOpen, "Load class parameters from script");
            if (_loadFoldIsOpen)
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

            //options
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _optionFoldIsOpen = EditorGUILayout.Foldout(_optionFoldIsOpen, "Options");
            if (_optionFoldIsOpen)
            {
                EditorGUI.indentLevel++;
                GUILayout.Label("Generate Set methods");
                _generateSetMethod = EditorGUILayout.Toggle(string.Empty, _generateSetMethod);
                EditorGUILayout.Space(10);
                GUILayout.Label("JSON deserialize mode (Experimental)");
                _jsonDeserializeMode = EditorGUILayout.Toggle(string.Empty, _jsonDeserializeMode);
                EditorGUILayout.Space(10);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

            //Generate button
            var canGenerate = !string.IsNullOrEmpty(_className) && _fieldList.Count > 0 && !string.IsNullOrEmpty(_fieldList[0]);
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                //Generate 
                if (GUILayout.Button("Generate DarkClass"))
                {
                    _generatedCode = GenerateDarkClass(_generateSetMethod);
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

        /*------------------------------Generate DarkClass-----------------------------*/

        private string GenerateDarkClass(bool generateSetMethod)
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
            if (!_jsonDeserializeMode)
            {
                // default mode
                var mainClassString = GenerateDefaultConstructor(pClassName, enumName, _fieldList);
                sb.Append(AddTabLines(mainClassString, nst + 1));
                sb.Append(GenerateClassFooter(nameSpaceIsExist));
                sb.AppendLine();
            }
            else
            {
                //json mode
                var commentForLoadScript = GenerateCommentForLoadScript(pClassName, _fieldList);
                sb.Append(AddTabLines(commentForLoadScript, nst + 1));
                var mainClassString = GenerateJsonConstructor(pClassName, enumName, _fieldList);
                sb.Append(AddTabLines(mainClassString, nst + 1));
                sb.Append(GenerateClassFooter(nameSpaceIsExist));
                sb.AppendLine();
            }

            //extension class
            var extensionClassHeaderString = GenerateExtensionClassHeader(pClassName);
            sb.Append(AddTabLines(extensionClassHeaderString, nst));
            sb.Append(AddTabLines("// Get methods\r\n", nst + 1));
            var getMethodsString = GenerateGetMethods(pClassName, enumName, _fieldList);
            sb.Append(AddTabLines(getMethodsString, nst + 1));
            if (generateSetMethod)
            {
                sb.AppendLine();
                sb.Append(AddTabLines("// Set methods\r\n", nst + 1));
                var setMethodsString = GenerateSetMethods(pClassName, enumName, _fieldList);
                sb.Append(AddTabLines(setMethodsString, nst + 1));
            }
            sb.Append(GenerateFooter(nameSpaceIsExist));

            //result
            return sb.ToString();
        }

        /*------------------------------Load script-----------------------------*/
        private string GenerateCommentForLoadScript(string pClassName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// This comments for loading this script by generator : ");
            sb.Append($"// public static {pClassName} New(");
            foreach (var field in fieldList)
            {
                var (argumentType, argumentName, _) = getTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                sb.Append($"{argumentType} {argumentName}, ");
            }
            sb.Remove(sb.Length - 2, 2);    //remove last comma and space
            sb.AppendLine($")");
            return sb.ToString();
        }

        /*------------------------------Generate Default Part Code-----------------------------*/
        private string GenerateClassFooter(bool nameSpaceIsExist)
        {
            return nameSpaceIsExist ? "\t}\r\n" : "}\r\n";
        }

        private string AddTabLines(string srcText, int tabNum)
        {
            var sb = new StringBuilder();
            var texts = srcText.Replace("\r\n", "\n").Split("\n");
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
            sb.AppendLine($"using VRC.SDK3.Data;");
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
            foreach (var field in _fieldList)
            {
                var (argumentType, _, pArgumentName) = getTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                sb.AppendLine($"\t{pArgumentName},");
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

        private string GenerateDefaultConstructor(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.Append($"public static {pClassName} New(");
            foreach (var field in fieldList)
            {
                var (argumentType, argumentName, _) = getTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                sb.Append($"{argumentType} {argumentName}, ");
            }
            sb.Remove(sb.Length - 2, 2);    //remove last comma and space
            sb.AppendLine($")");
            sb.AppendLine($"{{");

            //object 
            sb.Append(GenerateObjectAssign(pClassName, enumName, fieldList));
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
            foreach (var field in fieldList)
            {
                var (argumentType, _, pArgumentName) = getTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }

                sb.AppendLine($"public static {argumentType} {pArgumentName}(this {pClassName} instance)");
                sb.AppendLine($"\t=> ({argumentType})((object[])(object)instance)[(int){enumName}.{pArgumentName}];");
            }
            return sb.ToString();
        }

        private string GenerateSetMethods(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            foreach (var field in fieldList)
            {
                var (argumentType, _, pArgumentName) = getTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }

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

        private (string, string, string) getTypeAndNamesFromField(string field)
        {
            var args = Regex.Split(field.Trim(), @"\s+");
            if (args.Length != 2) { return (string.Empty, string.Empty, string.Empty); }
            var argumentType = args[0].Trim();
            var argumentName = args[1].Trim();
            var pArgumentName = ToPascal(argumentName);
            return (argumentType, argumentName, pArgumentName);
        }

        /*------------------------------Generate JSON Part Code-----------------------------*/

        private string GenerateJsonConstructor(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public static {pClassName} New(DataDictionary dic)");
            sb.AppendLine($"{{");
            foreach (var field in fieldList)
            {
                var jsonField = JsonFieldDeclaration(field);
                sb.AppendLine(jsonField);
            }

            //object 
            sb.Append(GenerateObjectAssign(pClassName, enumName, fieldList));
            return sb.ToString();
        }

        private string JsonFieldDeclaration(string field)
        {
            var (argumentType, argumentName, pArgumentName) = getTypeAndNamesFromField(field);
            if (string.IsNullOrEmpty(argumentType)) { return string.Empty; }

            //type of arrays (List is not supported)
            var isArray = Regex.IsMatch(argumentType, @".+\[\s*\]");
            if (isArray)
            {
                return GenerateJsonArrayField(argumentType, argumentName);
            }

            //type of defaults
            return GetCastedTypeAndNameFromField(argumentType, argumentName, isArray);

        }

        private string GenerateJsonArrayField(string argumentType, string argumentName)
        {
            var sb = new StringBuilder();

            var pureType = argumentType.Replace("[]", "");
            sb.AppendLine($"\tvar {argumentName}List = dic[\"{argumentName}\"].DataList;");
            sb.AppendLine($"\tvar {argumentName}Count = {argumentName}List.Count;");
            sb.AppendLine($"\tvar {argumentName} = new {pureType}[{argumentName}Count];");

            sb.AppendLine($"\tfor (int i = 0; i < {argumentName}Count; i++)");
            sb.AppendLine($"\t{{");
            sb.Append(GetCastedTypeAndNameFromField(pureType, argumentName, true));
            sb.AppendLine($"\t}}");

            return sb.ToString();
        }

        private string GetCastedTypeAndNameFromField(string argumentType, string argumentName, bool isArrayElement)
        {
            var sb = new StringBuilder();
            var leftPreString = isArrayElement ? "\t\t" : "\tvar ";
            var leftPostString = isArrayElement ? "[i]" : "";
            var newLine = isArrayElement ? "\r\n" : "";
            sb.Append($"{leftPreString}{argumentName}{leftPostString} = ");
            var rightString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{argumentName}\"]";

            switch (argumentType)
            {
                case "bool":
                    sb.Append($"{rightString}.Boolean;");
                    break;
                case "char":
                case "string":
                    sb.Append($"{rightString}.String;" + newLine);
                    break;
                case "byte":
                case "sbyte":
                case "decimal":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "short":
                case "ushort":
                case "float":
                case "double":
                    sb.Append($"({argumentType}){rightString}.Number;" + newLine);
                    break;
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                    sb.Append(GenerateJsonVectorField(argumentType, argumentName, isArrayElement));
                    break;
                case "Color":
                case "Color32":
                    sb.Append(GenerateJsonColorField(argumentType, argumentName, isArrayElement));
                    break;
                case "TrackingDataType":
                case "HumanBodyBones":
                case "TextureWrapMode":
                case "GraphicsFormat":
                case "TextureFormat":
                    //enum
                    sb.Append($"({argumentType})(int){rightString}.Number;" + newLine);
                    break;
                default:
                    //Dark class
                    sb.Append($"({argumentType}){rightString}.New(dic[\"{argumentName}\"].DataDictionary);" + newLine);
                    break;
            }
            return sb.ToString();
        }

        private string GenerateJsonColorField(string argumentType, string argumentName, bool isArrayElement)
        {
            var sb = new StringBuilder();
            var cast = string.Empty;
            if (argumentType == "Color")
            {
                sb.AppendLine($"new Color();");
                cast = "(float)";
            }
            else if (argumentType == "Color32")
            {
                sb.AppendLine($"new Color32();");
                cast = "(byte)";
            }

            var tab = isArrayElement ? $"\t\t" : $"\t";
            var arrayElement = isArrayElement ? $"[i]" : $"";
            var elementRightString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{argumentName}\"]";
            sb.AppendLine($"{tab}var {argumentName}Dic = {elementRightString}.DataDictionary;");

            sb.AppendLine($"{tab}{argumentName}{arrayElement}.r = {cast}{argumentName}Dic[\"r\"].Number;");
            sb.AppendLine($"{tab}{argumentName}{arrayElement}.g = {cast}{argumentName}Dic[\"g\"].Number;");
            sb.AppendLine($"{tab}{argumentName}{arrayElement}.b = {cast}{argumentName}Dic[\"b\"].Number;");
            sb.AppendLine($"{tab}{argumentName}{arrayElement}.a = {cast}{argumentName}Dic[\"a\"].Number;");
            return sb.ToString();
        }


        private string GenerateJsonVectorField(string argumentType, string argumentName, bool isArrayElement)
        {
            var sb = new StringBuilder();
            if (argumentType == "Vector2")
            {
                sb.AppendLine($"new Vector2();");
            }
            else if (argumentType == "Vector3")
            {
                sb.AppendLine($"new Vector3();");
            }
            else if (argumentType == "Vector4")
            {
                sb.AppendLine($"new Vector4();");
            }
            else if (argumentType == "Quaternion")
            {
                sb.AppendLine($"new Quaternion();");
            }

            var tab = isArrayElement ? $"\t\t" : $"\t";
            var arrayElement = isArrayElement ? $"[i]" : $"";
            var elementRightString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{argumentName}\"]";
            sb.AppendLine($"{tab}var {argumentName}Dic = {elementRightString}.DataDictionary;");

            sb.AppendLine($"{tab}{argumentName}{arrayElement}.x = (float){argumentName}Dic[\"x\"].Number;");
            sb.AppendLine($"{tab}{argumentName}{arrayElement}.y = (float){argumentName}Dic[\"y\"].Number;");
            if (argumentType == "Vector2") { return sb.ToString(); }
            sb.AppendLine($"{tab}{argumentName}{arrayElement}.z = (float){argumentName}Dic[\"z\"].Number;");
            if (argumentType == "Vector3") { return sb.ToString(); }
            sb.AppendLine($"{tab}{argumentName}{arrayElement}.w = (float){argumentName}Dic[\"w\"].Number;");
            return sb.ToString();
        }

        /*------------------------------Utility----------------------------------*/


        private string GenerateObjectAssign(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();

            //object 
            sb.AppendLine($"\t// Make objects");
            sb.AppendLine($"\tvar buff = new object[(int){enumName}.Count];");
            sb.AppendLine();

            var buffBase = $"\tbuff[(int)";
            foreach (var field in fieldList)
            {
                var (argumentType, argumentName, pArgumentName) = getTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }

                sb.AppendLine($"{buffBase}{enumName}.{pArgumentName}] = {argumentName};");
            }

            sb.AppendLine();
            sb.AppendLine($"\treturn ({pClassName})(object)buff;");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private void LoadScript(string text)
        {
            Debug.Log("LoadScript: " + text);
            var oneLineText = text.Replace("\r\n", "\n").Replace("\n", " "); //remove line break and join with space

            //namespace
            _nameSpace = ExtractNameSpace(oneLineText);

            //className
            _className = ExtractClassName(oneLineText);

            //Fields
            if (!string.IsNullOrEmpty(_className))
            {
                var fieldStrings = ExtractFields(oneLineText, _className);
                if (fieldStrings.Count > 0) { UpdateReorderableList(fieldStrings); }
            }
        }

        private string ExtractNameSpace(string oneLineText)
        {
            var matches = Regex.Matches(oneLineText, @"\s*namespace\s+[^\s]+\s+");
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

        private string ExtractClassName(string oneLineText)
        {
            var matches = Regex.Matches(oneLineText, @"\s*public\s+class\s+[^\s]+\s+:");
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

        private List<string> ExtractFields(string oneLineText, string className)
        {
            var fields = new List<string>();

            var matches = Regex.Matches(oneLineText, @"\s*public\s+static\s+" + className + @"\s+New\s*\([^\)]+\)");
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

        private static string ToPascal(string text)
        {
            return Regex.Replace(text.Replace("_", " "), @"\b[a-z]", match => match.Value.ToUpper()).Replace(" ", "");
        }
    }
}
#endif