using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DataCenter.Editor
{
    /// <summary>
    /// Reports the structure, size and triangle count of the source data center model, so the
    /// builder's placement and the performance budget can be checked without opening the Editor.
    /// </summary>
    static class SourceModelProbe
    {
        [MenuItem("Tools/Data Center/Report Source Model", false, 100)]
        public static void Report()
        {
            var text = Describe(BuilderAssets.SourceModelPath);
            Debug.Log(text);
            File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/DataCenterSourceModel.txt"), text);
        }

        /// <summary>Batch-mode entry point: <c>-executeMethod DataCenter.Editor.SourceModelProbe.ReportBatch</c>.</summary>
        public static void ReportBatch()
        {
            Report();
            EditorApplication.Exit(0);
        }

        static string Describe(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                return $"Source model not found at {path}";

            var builder = new StringBuilder();
            long total = 0;
            builder.AppendLine($"{path}: rotation {model.transform.localRotation.eulerAngles} scale {model.transform.localScale}");

            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                var filter = t.GetComponent<MeshFilter>();
                var renderer = t.GetComponent<Renderer>();
                long triangles = 0;
                if (filter != null && filter.sharedMesh != null)
                {
                    for (var i = 0; i < filter.sharedMesh.subMeshCount; i++)
                        triangles += filter.sharedMesh.GetIndexCount(i) / 3;
                }

                total += triangles;
                builder.Append($"{t.name} (parent {(t.parent != null ? t.parent.name : "-")}) pos {t.localPosition} rot {t.localRotation.eulerAngles} scale {t.localScale} tris {triangles}");
                if (renderer != null)
                {
                    builder.Append($" bounds {renderer.bounds.center} size {renderer.bounds.size} materials ");
                    foreach (var material in renderer.sharedMaterials)
                        builder.Append(material != null ? $"{material.name}[{(material.mainTexture != null ? material.mainTexture.name : "")}] " : "null ");
                }

                builder.AppendLine();
            }

            builder.AppendLine($"Total triangles: {total}");
            return builder.ToString();
        }
    }
}
