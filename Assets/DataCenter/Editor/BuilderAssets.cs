using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DataCenter.Robotics;
using DataCenter.Tasks;

namespace DataCenter.Editor
{
    /// <summary>
    /// Creates or refreshes every generated asset the scene builder needs: layers, materials,
    /// textures, meshes and ScriptableObjects. Everything lands under
    /// <see cref="GeneratedRoot"/>, so a rebuild never touches hand-made assets.
    /// </summary>
    static class BuilderAssets
    {
        public const string Root = "Assets/DataCenter";
        public const string GeneratedRoot = Root + "/Generated";
        public const string MaterialsFolder = GeneratedRoot + "/Materials";
        public const string TexturesFolder = GeneratedRoot + "/Textures";
        public const string MeshesFolder = GeneratedRoot + "/Meshes";
        public const string PrefabsFolder = Root + "/Prefabs";
        public const string DataFolder = Root + "/Data";
        public const string ScenesFolder = Root + "/Scenes";
        public const string UIFolder = Root + "/UI";

        public const string SourceModelPath = "Assets/Data Centers/Data Server System Equipment 2_fbx 2.FBX";

        // Layers the simulator relies on. Added to the first free user slots if missing.
        public const string RobotLayer = "Robot";
        public const string EquipmentLayer = "Equipment";
        public const string CableLayer = "Cable";
        public const string PortLayer = "Port";
        public const string HandleLayer = "TeleopHandle";

        static readonly Dictionary<string, Material> s_Materials = new Dictionary<string, Material>();

        public static void EnsureFolders()
        {
            foreach (var folder in new[] { GeneratedRoot, MaterialsFolder, TexturesFolder, MeshesFolder, PrefabsFolder, DataFolder, DataFolder + "/Specs", ScenesFolder, GeneratedRoot + "/UI", GeneratedRoot + "/NavMesh" })
                EnsureFolder(folder);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------------------------------------------------------------- Layers

        public static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            foreach (var name in new[] { RobotLayer, EquipmentLayer, CableLayer, PortLayer, HandleLayer })
            {
                if (LayerMask.NameToLayer(name) >= 0)
                    continue;

                for (var i = 8; i < layers.arraySize; i++)
                {
                    var slot = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(slot.stringValue))
                        continue;

                    slot.stringValue = name;
                    Debug.Log($"[Data Center] Added layer '{name}' at index {i}.");
                    break;
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        public static int Layer(string name)
        {
            var layer = LayerMask.NameToLayer(name);
            if (layer < 0)
                throw new System.InvalidOperationException($"Layer '{name}' is missing. Run EnsureLayers first.");
            return layer;
        }

        public static LayerMask Mask(params string[] names) => LayerMask.GetMask(names);

        // ---------------------------------------------------------------- Materials

        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");
        static Shader Unlit => Shader.Find("Universal Render Pipeline/Unlit");
        static Shader Led => Shader.Find("DataCenter/Status LED");

        /// <summary>A URP Lit material, created once and refreshed on rebuild.</summary>
        public static Material LitMaterial(string name, Color color, float metallic = 0f, float smoothness = 0.35f, Texture2D texture = null, Vector2? tiling = null)
        {
            var material = GetOrCreateMaterial(name, Lit);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A URP Unlit material, for emissive panels, signage backs and UI-like world objects.</summary>
        public static Material UnlitMaterial(string name, Color color, Texture2D texture = null)
        {
            var material = GetOrCreateMaterial(name, Unlit);
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>An LED material. <paramref name="vertexTint"/> reads colour and blink phase from the mesh.</summary>
        public static Material LedMaterial(string name, Color color, bool vertexTint, float intensity = 1.6f)
        {
            var material = GetOrCreateMaterial(name, Led);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Intensity", intensity);
            material.SetFloat("_VertexTint", vertexTint ? 1f : 0f);
            if (vertexTint)
                material.EnableKeyword("_VERTEX_TINT");
            else
                material.DisableKeyword("_VERTEX_TINT");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetOrCreateMaterial(string name, Shader shader)
        {
            if (s_Materials.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            s_Materials[name] = material;
            return material;
        }

        public static void ClearCache() => s_Materials.Clear();

        // ---------------------------------------------------------------- Textures

        /// <summary>Raised-floor tile: light grey panel with a dark seam, one tile per texture.</summary>
        public static Texture2D FloorTileTexture(bool perforated)
        {
            var name = perforated ? "FloorTilePerforated" : "FloorTile";
            return SaveTexture(name, 256, 256, (x, y) =>
            {
                var seam = x < 3 || y < 3 || x > 252 || y > 252;
                if (seam)
                    return new Color(0.18f, 0.19f, 0.21f);

                var bevel = x < 8 || y < 8 || x > 247 || y > 247;
                var baseColor = bevel ? new Color(0.62f, 0.64f, 0.66f) : new Color(0.71f, 0.72f, 0.74f);

                if (perforated && x > 24 && x < 232 && y > 24 && y < 232)
                {
                    // A grid of small holes, as on a cold-aisle airflow tile.
                    var cx = (x - 24) % 13 - 6;
                    var cy = (y - 24) % 13 - 6;
                    if (cx * cx + cy * cy < 10)
                        return new Color(0.25f, 0.27f, 0.3f);
                }

                // Faint speckle so large floors do not look like flat plastic.
                var noise = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.04f - 0.02f;
                return baseColor + new Color(noise, noise, noise);
            });
        }

        /// <summary>Rack door: a perforated mesh over server bezels. Tiles once per door.</summary>
        public static Texture2D RackFrontTexture()
        {
            return SaveTexture("RackFront", 128, 512, (x, y) =>
            {
                // 42 units over 512 px; servers occupy 1U or 2U bands with darker gaps.
                var unit = y / (512f / 42f);
                var inUnit = unit - Mathf.Floor(unit);
                var server = Mathf.FloorToInt(unit) % 7 != 6;
                var color = server ? new Color(0.13f, 0.14f, 0.16f) : new Color(0.06f, 0.065f, 0.075f);
                if (server && inUnit < 0.08f)
                    color = new Color(0.08f, 0.085f, 0.095f);

                // Perforation: a fine dot grid lightens the face, as light catches the door mesh.
                if ((x % 4 == 0) && (y % 4 == 0))
                    color += new Color(0.05f, 0.05f, 0.055f);

                // Door frame.
                if (x < 5 || x > 122 || y < 5 || y > 506)
                    color = new Color(0.09f, 0.095f, 0.105f);
                return color;
            });
        }

        /// <summary>Diagonal safety stripes for corridor edges.</summary>
        public static Texture2D HazardTexture()
        {
            return SaveTexture("Hazard", 64, 64, (x, y) =>
                ((x + y) / 16) % 2 == 0 ? new Color(0.95f, 0.75f, 0.05f) : new Color(0.08f, 0.08f, 0.08f));
        }

        static Texture2D SaveTexture(string name, int width, int height, System.Func<int, int, Color> pixel)
        {
            var path = $"{TexturesFolder}/{name}.png";
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(x, y);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = Mathf.Max(width, height);
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- Meshes

        /// <summary>
        /// A rack's worth of server activity LEDs as one mesh: small quads on the door face with
        /// colour in vertex RGB and blink phase in vertex alpha, read by the Status LED shader.
        /// </summary>
        /// <param name="variant">Seed. A few variants keep neighbouring racks from looking cloned.</param>
        public static Mesh RackLedMesh(int variant)
        {
            var path = $"{MeshesFolder}/RackLeds_{variant}.asset";
            var random = new System.Random(1000 + variant);
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            var palette = new[]
            {
                new Color(0.2f, 1f, 0.35f),
                new Color(0.2f, 1f, 0.35f),
                new Color(0.2f, 1f, 0.35f),
                new Color(0.25f, 0.6f, 1f),
                new Color(1f, 0.65f, 0.1f),
            };

            const float uBottom = 0.08f;
            const float uHeight = 0.04445f;
            const float size = 0.0055f;

            for (var unit = 0; unit < 42; unit++)
            {
                // Leave some units empty, as real racks are never full.
                if (random.NextDouble() < 0.18)
                    continue;

                var y = uBottom + unit * uHeight + uHeight * 0.5f;
                var count = 1 + random.Next(3);
                for (var i = 0; i < count; i++)
                {
                    var x = 0.2f - i * 0.016f - (float)random.NextDouble() * 0.004f;
                    var color = palette[random.Next(palette.Length)];
                    color.a = (float)random.NextDouble();
                    AddQuad(vertices, colors, triangles, new Vector3(x, y, 0f), size, color);
                }

                // A second cluster on the left for some servers.
                if (random.NextDouble() < 0.35)
                {
                    var color = palette[0];
                    color.a = (float)random.NextDouble();
                    AddQuad(vertices, colors, triangles, new Vector3(-0.21f, y, 0f), size, color);
                }
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var isNew = mesh == null;
            if (isNew)
                mesh = new Mesh { name = $"RackLeds_{variant}" };

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            if (isNew)
                AssetDatabase.CreateAsset(mesh, path);
            else
                EditorUtility.SetDirty(mesh);

            return mesh;
        }

        static void AddQuad(List<Vector3> vertices, List<Color> colors, List<int> triangles, Vector3 centre, float size, Color color)
        {
            var start = vertices.Count;
            var h = size * 0.5f;
            vertices.Add(centre + new Vector3(-h, -h, 0f));
            vertices.Add(centre + new Vector3(h, -h, 0f));
            vertices.Add(centre + new Vector3(h, h, 0f));
            vertices.Add(centre + new Vector3(-h, h, 0f));
            for (var i = 0; i < 4; i++)
                colors.Add(color);

            // Facing +Z, the rack's front.
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        // ---------------------------------------------------------------- ScriptableObjects

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            // A file that exists but does not load as T has lost its script reference (for example
            // after a class moved files). Recreate it rather than fail on the existing path.
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || System.IO.File.Exists(path))
                AssetDatabase.DeleteAsset(path);

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static PortType PortTypeAsset(string code, string displayName, Color cableColor, float diameter, float radius, float angle)
        {
            var asset = LoadOrCreate<PortType>($"{DataFolder}/Specs/PortType_{code}.asset");
            asset.code = code;
            asset.displayName = displayName;
            asset.cableColor = cableColor;
            asset.cableDiameter = diameter;
            asset.captureRadius = radius;
            asset.captureAngle = angle;
            (asset.plainName, asset.description) = PlainLanguage(code);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>Everyday name and a one-line explanation of each connector, for the operator.</summary>
        public static (string plainName, string description) PlainLanguage(string code)
        {
            switch (code)
            {
                case "LC":
                    return ("fiber-optic cable", "LC is a small fiber-optic plug. The cable carries data as pulses of light, so it is thin and needs gentle handling.");
                case "C13":
                    return ("power cable", "C13 is the standard plug on a computer power cord, the same kind that powers a desktop PC.");
                default:
                    return ("network cable", "RJ45 is the standard network (Ethernet) plug, the same clip-in connector as on a laptop or home router cable.");
            }
        }

        public static RobotSpec RobotSpecAsset()
        {
            // Values are only written when the asset is first created, so tuning in the inspector survives rebuilds.
            var path = $"{DataFolder}/Specs/RobotSpec_MR6.asset";
            var existing = AssetDatabase.LoadAssetAtPath<RobotSpec>(path);
            if (existing != null)
                return existing;

            var spec = LoadOrCreate<RobotSpec>(path);
            spec.modelName = "MR-6 Mobile Manipulator";
            EditorUtility.SetDirty(spec);
            return spec;
        }

        public static WorkOrderSet WorkOrderSetAsset(string sectionId)
        {
            var set = LoadOrCreate<WorkOrderSet>($"{DataFolder}/WorkOrders/WorkOrders_{sectionId}.asset");
            set.sectionId = sectionId;
            set.json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataFolder}/WorkOrders/Section{sectionId}.json");
            EditorUtility.SetDirty(set);
            return set;
        }
    }
}
