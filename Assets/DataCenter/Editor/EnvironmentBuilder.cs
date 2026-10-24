using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using DataCenter.Tasks;
using static DataCenter.Editor.RobotBuilder;

namespace DataCenter.Editor
{
    /// <summary>Where a section sits in the hall and what it contains.</summary>
    class SectionLayout
    {
        public string id;
        public string title;
        public Rect area;        // x/z extents on the floor
        public Color accent;
    }

    /// <summary>
    /// Builds the hall shell and the equipment in it: raised floor, walls, ceiling lights, rack
    /// rows with hot-aisle containment, overhead cable trays, signage, the interactive work rack and
    /// the cable cart.
    /// </summary>
    /// <remarks>
    /// Hall coordinates: 40 m along X, 25 m along Z, floor top at y = 0. A 1.6 m main corridor runs
    /// along X through the middle; Server Hall (A) and Network Core (B) are north of it, Staging (D)
    /// and Power (C) south.
    /// </remarks>
    static class EnvironmentBuilder
    {
        public const float HallHalfX = 20f;
        public const float HallHalfZ = 12.5f;
        public const float CeilingHeight = 4.2f;
        public const float CorridorHalfWidth = 0.8f;

        // Rack geometry, in metres. Origin on the floor, centred; front faces +Z.
        public const float RackWidth = 0.6f;
        public const float RackDepth = 1.1f;
        public const float RackHeight = 2.0f;
        public const float UnitBottom = 0.08f;
        public const float UnitHeight = 0.04445f;
        public const float PanelFace = 0.53f;

        public static readonly SectionLayout[] Sections =
        {
            new SectionLayout { id = "A", title = "SERVER HALL", area = Rect.MinMaxRect(-HallHalfX, CorridorHalfWidth, 1f, HallHalfZ), accent = new Color(0.1f, 0.83f, 1f) },
            new SectionLayout { id = "B", title = "NETWORK CORE", area = Rect.MinMaxRect(1f, CorridorHalfWidth, HallHalfX, HallHalfZ), accent = new Color(0.65f, 0.45f, 1f) },
            new SectionLayout { id = "C", title = "POWER ROOM", area = Rect.MinMaxRect(1f, -HallHalfZ, HallHalfX, -CorridorHalfWidth), accent = new Color(1f, 0.69f, 0.13f) },
            new SectionLayout { id = "D", title = "STAGING", area = Rect.MinMaxRect(-HallHalfX, -HallHalfZ, 1f, -CorridorHalfWidth), accent = new Color(0.18f, 0.9f, 0.42f) },
        };

        static Material s_Floor, s_FloorPerforated, s_Wall, s_Skirting, s_Ceiling, s_LightPanel, s_RackBody, s_RackDoor, s_Metal, s_Dark;
        static Material s_TrayYellow, s_TrayGrey, s_Containment, s_Hazard, s_PanelGrey, s_SwitchBody, s_PortHole, s_PortLed, s_ServerLed, s_SignBack, s_White;

        public static void CreateMaterials()
        {
            s_Floor = BuilderAssets.LitMaterial("Floor_Tile", Color.white, 0f, 0.25f, BuilderAssets.FloorTileTexture(false));
            s_FloorPerforated = BuilderAssets.LitMaterial("Floor_Perforated", Color.white, 0f, 0.25f, BuilderAssets.FloorTileTexture(true));
            s_Wall = BuilderAssets.LitMaterial("Wall", new Color(0.78f, 0.8f, 0.82f), 0f, 0.15f);
            s_Skirting = BuilderAssets.LitMaterial("Skirting", new Color(0.2f, 0.22f, 0.25f), 0f, 0.3f);
            s_Ceiling = BuilderAssets.LitMaterial("Ceiling", new Color(0.32f, 0.34f, 0.37f), 0f, 0.1f);
            s_LightPanel = BuilderAssets.UnlitMaterial("Light_Panel", new Color(0.95f, 0.97f, 1f));
            s_RackBody = BuilderAssets.LitMaterial("Rack_Body", new Color(0.09f, 0.095f, 0.11f), 0.4f, 0.45f);
            s_RackDoor = BuilderAssets.LitMaterial("Rack_Door", Color.white, 0.5f, 0.4f, BuilderAssets.RackFrontTexture());
            s_Metal = BuilderAssets.LitMaterial("Metal", new Color(0.6f, 0.62f, 0.65f), 0.85f, 0.55f);
            s_Dark = BuilderAssets.LitMaterial("Dark", new Color(0.05f, 0.055f, 0.06f), 0.2f, 0.3f);
            s_TrayYellow = BuilderAssets.LitMaterial("Tray_Fiber", new Color(1f, 0.78f, 0.05f), 0f, 0.4f);
            s_TrayGrey = BuilderAssets.LitMaterial("Tray_Basket", new Color(0.55f, 0.57f, 0.6f), 0.7f, 0.4f);
            s_Containment = BuilderAssets.LitMaterial("Containment", new Color(0.36f, 0.42f, 0.5f), 0.2f, 0.7f);
            s_Hazard = BuilderAssets.LitMaterial("Hazard", Color.white, 0f, 0.3f, BuilderAssets.HazardTexture());
            s_PanelGrey = BuilderAssets.LitMaterial("Patch_Panel", new Color(0.7f, 0.72f, 0.74f), 0.6f, 0.45f);
            s_SwitchBody = BuilderAssets.LitMaterial("Switch_Body", new Color(0.16f, 0.2f, 0.26f), 0.5f, 0.45f);
            s_PortHole = BuilderAssets.LitMaterial("Port_Hole", new Color(0.02f, 0.02f, 0.025f), 0f, 0.1f);
            s_PortLed = BuilderAssets.LedMaterial("LED_Port", new Color(0.06f, 0.07f, 0.08f), false, 1.5f);
            s_ServerLed = BuilderAssets.LedMaterial("LED_Server", Color.white, true, 1.6f);
            s_SignBack = BuilderAssets.LitMaterial("Sign_Back", new Color(0.06f, 0.08f, 0.11f), 0.1f, 0.5f);
            s_White = BuilderAssets.LitMaterial("White", new Color(0.92f, 0.93f, 0.94f), 0f, 0.4f);
        }

        // ---------------------------------------------------------------- Hall

        public static Transform BuildHall(Transform parent)
        {
            var hall = Child("Hall", parent, Vector3.zero);
            var floorLayer = 0;

            // Floor: one slab per section plus the corridor, so tiling stays one texture per 0.6 m tile.
            var floor = Child("Floor", hall, Vector3.zero);
            FloorSlab("Corridor", floor, Rect.MinMaxRect(-HallHalfX, -CorridorHalfWidth, HallHalfX, CorridorHalfWidth), s_Floor, floorLayer);
            foreach (var section in Sections)
                FloorSlab($"Section {section.id}", floor, section.area, s_Floor, floorLayer);

            // Corridor edge stripes.
            Box("Corridor Edge N", floor, new Vector3(0f, 0.002f, CorridorHalfWidth - 0.04f), new Vector3(HallHalfX * 2f, 0.002f, 0.08f), s_Hazard);
            Box("Corridor Edge S", floor, new Vector3(0f, 0.002f, -CorridorHalfWidth + 0.04f), new Vector3(HallHalfX * 2f, 0.002f, 0.08f), s_Hazard);

            // Walls.
            var walls = Child("Walls", hall, Vector3.zero);
            Wall(walls, "North", new Vector3(0f, 0f, HallHalfZ + 0.1f), new Vector3(HallHalfX * 2f + 0.4f, CeilingHeight, 0.2f));
            Wall(walls, "South", new Vector3(0f, 0f, -HallHalfZ - 0.1f), new Vector3(HallHalfX * 2f + 0.4f, CeilingHeight, 0.2f));
            Wall(walls, "East", new Vector3(HallHalfX + 0.1f, 0f, 0f), new Vector3(0.2f, CeilingHeight, HallHalfZ * 2f));
            Wall(walls, "West", new Vector3(-HallHalfX - 0.1f, 0f, 0f), new Vector3(0.2f, CeilingHeight, HallHalfZ * 2f));

            // Ceiling with a grid of light panels. Unlit quads read as light without costing a light.
            var ceiling = Child("Ceiling", hall, Vector3.zero);
            Box("Ceiling Slab", ceiling, new Vector3(0f, CeilingHeight + 0.05f, 0f), new Vector3(HallHalfX * 2f, 0.1f, HallHalfZ * 2f), s_Ceiling);
            for (var x = -HallHalfX + 1.6f; x < HallHalfX; x += 3.2f)
            {
                for (var z = -HallHalfZ + 1.6f; z < HallHalfZ; z += 3.0f)
                    Box("Light", ceiling, new Vector3(x, CeilingHeight - 0.01f, z), new Vector3(0.6f, 0.02f, 1.2f), s_LightPanel);
            }

            return hall;
        }

        static void FloorSlab(string name, Transform parent, Rect area, Material material, int layer)
        {
            var slab = Box(name, parent, new Vector3(area.center.x, -0.05f, area.center.y), new Vector3(area.width, 0.1f, area.height), material, true);
            slab.layer = layer;

            // One texture repeat per 0.6 m tile. Box UVs span each face, so scale the material per slab.
            var renderer = slab.GetComponent<MeshRenderer>();
            var instance = new Material(material) { name = $"{material.name}_{name}" };
            instance.SetTextureScale("_BaseMap", new Vector2(area.width / 0.6f, area.height / 0.6f));
            var path = $"{BuilderAssets.MaterialsFolder}/{instance.name.Replace(' ', '_')}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(instance, path);
            renderer.sharedMaterial = instance;
        }

        static void Wall(Transform parent, string name, Vector3 basePosition, Vector3 size)
        {
            var wall = Box($"Wall {name}", parent, basePosition + Vector3.up * (size.y * 0.5f), size, s_Wall, true);
            wall.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
            var skirtSize = new Vector3(size.x + 0.02f, 0.12f, size.z + 0.02f);
            Box($"Skirting {name}", parent, basePosition + Vector3.up * 0.06f, skirtSize, s_Skirting);
        }

        // ---------------------------------------------------------------- Rack prefabs

        /// <summary>
        /// Builds the decorative 42U rack prefab with two LODs. Static batching merges instances; the
        /// LED mesh variant keeps neighbours from blinking alike.
        /// </summary>
        public static GameObject BuildRackPrefab(int variant)
        {
            var root = new GameObject($"Rack_42U_{variant}");
            root.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, RackHeight * 0.5f, 0f);
            collider.size = new Vector3(RackWidth, RackHeight, RackDepth);

            var ledMesh = BuilderAssets.RackLedMesh(variant);

            var lod0 = Child("LOD0", root.transform, Vector3.zero);
            Box("Body", lod0, new Vector3(0f, RackHeight * 0.5f, 0f), new Vector3(RackWidth, RackHeight, RackDepth), s_RackBody);
            Box("Door", lod0, new Vector3(0f, RackHeight * 0.5f + 0.01f, RackDepth * 0.5f + 0.006f), new Vector3(RackWidth - 0.03f, RackHeight - 0.08f, 0.012f), s_RackDoor);
            Box("Handle", lod0, new Vector3(-0.245f, 1.05f, RackDepth * 0.5f + 0.02f), new Vector3(0.014f, 0.14f, 0.02f), s_Metal);
            Box("Roof Vent", lod0, new Vector3(0f, RackHeight + 0.004f, 0f), new Vector3(0.48f, 0.008f, 0.9f), s_Dark);
            Box("Plinth", lod0, new Vector3(0f, 0.025f, 0f), new Vector3(RackWidth + 0.01f, 0.05f, RackDepth + 0.01f), s_Dark);
            Leds("LEDs", lod0, ledMesh, new Vector3(0f, 0f, RackDepth * 0.5f + 0.0125f));

            var lod1 = Child("LOD1", root.transform, Vector3.zero);
            Box("Body", lod1, new Vector3(0f, RackHeight * 0.5f, 0f), new Vector3(RackWidth, RackHeight, RackDepth), s_RackBody);
            Box("Door", lod1, new Vector3(0f, RackHeight * 0.5f + 0.01f, RackDepth * 0.5f + 0.006f), new Vector3(RackWidth - 0.03f, RackHeight - 0.08f, 0.012f), s_RackDoor);
            Leds("LEDs", lod1, ledMesh, new Vector3(0f, 0f, RackDepth * 0.5f + 0.0125f));

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.18f, lod0.GetComponentsInChildren<Renderer>()),
                new LOD(0.015f, lod1.GetComponentsInChildren<Renderer>()),
            });
            group.RecalculateBounds();

            SetLayerRecursively(root, BuilderAssets.Layer(BuilderAssets.EquipmentLayer));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{BuilderAssets.PrefabsFolder}/Rack_42U_{variant}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject Leds(string name, Transform parent, Mesh mesh, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = s_ServerLed;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // ---------------------------------------------------------------- Section A

        /// <summary>Row origin and facing for a rack row. Racks run along +X from <paramref name="start"/>.</summary>
        public struct RowSpec
        {
            public string name;
            public Vector3 start;
            public bool facesNorth;
            public int count;
            public int firstNumber;
        }

        public static readonly RowSpec[] SectionARows =
        {
            new RowSpec { name = "A1", start = new Vector3(-15f, 0f, 2.6f), facesNorth = true, count = 10, firstNumber = 1 },
            new RowSpec { name = "A2", start = new Vector3(-15f, 0f, 5.9f), facesNorth = false, count = 10, firstNumber = 11 },
            new RowSpec { name = "A3", start = new Vector3(-15f, 0f, 8.25f), facesNorth = true, count = 10, firstNumber = 21 },
            new RowSpec { name = "A4", start = new Vector3(-15f, 0f, 11.55f), facesNorth = false, count = 10, firstNumber = 31 },
        };

        /// <summary>Rack number in section A that the robot works on.</summary>
        public const int WorkRackNumber = 3;

        /// <summary>World position of the centre of rack <paramref name="index"/> in a row.</summary>
        public static Vector3 RackPosition(RowSpec row, int index) => row.start + new Vector3(RackWidth * 0.5f + index * RackWidth, 0f, 0f);

        public static Quaternion RackRotation(RowSpec row) => Quaternion.Euler(0f, row.facesNorth ? 0f : 180f, 0f);

        public static Transform BuildSectionA(Transform parent, GameObject[] rackPrefabs, PortType rj45)
        {
            var section = Child("Section A - Server Hall", parent, Vector3.zero);
            var layout = Sections[0];

            // Cold aisles get perforated tiles, which is where the chilled air comes up.
            var floor = Child("Aisle Tiles", section, Vector3.zero);
            AisleTiles(floor, "Cold Aisle A1-A2", Rect.MinMaxRect(-15f, 3.15f, -9f, 5.35f));
            AisleTiles(floor, "Cold Aisle A3-A4", Rect.MinMaxRect(-15f, 8.8f, -9f, 11f));

            var racks = Child("Racks", section, Vector3.zero);
            Transform workRack = null;
            var variant = 0;
            foreach (var row in SectionARows)
            {
                var rowRoot = Child($"Row {row.name}", racks, Vector3.zero);
                for (var i = 0; i < row.count; i++)
                {
                    var number = row.firstNumber + i;
                    var position = RackPosition(row, i);
                    var rotation = RackRotation(row);

                    if (number == WorkRackNumber)
                    {
                        workRack = BuildWorkRack(rowRoot, $"A-R{number:00}", position, rotation, rj45);
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(rackPrefabs[variant++ % rackPrefabs.Length], rowRoot);
                    instance.name = $"A-R{number:00}";
                    instance.transform.SetPositionAndRotation(position, rotation);
                }

                RowEndLabel(rowRoot, row, layout.accent);
            }

            // Hot aisles are contained: a roof and end doors keep exhaust air from mixing back in.
            var containment = Child("Hot Aisle Containment", section, Vector3.zero);
            HotAisle(containment, "A2-A3", -15f, -9f, 6.45f, 7.7f);
            HotAisle(containment, "A1", -15f, -9f, 1.0f, 2.05f);

            var trays = Child("Cable Trays", section, Vector3.zero);
            foreach (var row in SectionARows)
                TrayRun(trays, row);

            var signs = Child("Signage", section, Vector3.zero);
            HangingSign(signs, "A  ·  SERVER HALL", "Cold aisle A1-A2   ·   Racks R01-R20", new Vector3(-15.9f, 2.9f, 4.25f), Quaternion.Euler(0f, -90f, 0f), layout.accent);
            HangingSign(signs, "A  ·  SERVER HALL", "Copper patching  ·  RJ45", new Vector3(-10f, 3.1f, CorridorHalfWidth + 0.05f), Quaternion.identity, layout.accent);

            return workRack;
        }

        static void AisleTiles(Transform parent, string name, Rect area)
        {
            var tiles = Box(name, parent, new Vector3(area.center.x, 0.001f, area.center.y), new Vector3(area.width, 0.002f, area.height), s_FloorPerforated);
            var instance = new Material(s_FloorPerforated) { name = $"Floor_Perforated_{name.Replace(' ', '_')}" };
            instance.SetTextureScale("_BaseMap", new Vector2(area.width / 0.6f, area.height / 0.6f));
            var path = $"{BuilderAssets.MaterialsFolder}/{instance.name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(instance, path);
            tiles.GetComponent<MeshRenderer>().sharedMaterial = instance;
        }

        static void HotAisle(Transform parent, string name, float xMin, float xMax, float zMin, float zMax)
        {
            var root = Child($"Hot Aisle {name}", parent, Vector3.zero);
            var width = xMax - xMin;
            var depth = zMax - zMin;
            var centre = new Vector3((xMin + xMax) * 0.5f, 0f, (zMin + zMax) * 0.5f);

            Box("Roof", root, centre + Vector3.up * (RackHeight + 0.03f), new Vector3(width, 0.03f, depth), s_Containment);
            for (var x = xMin + 1.2f; x < xMax; x += 1.2f)
                Box("Roof Rib", root, new Vector3(x, RackHeight + 0.05f, centre.z), new Vector3(0.04f, 0.02f, depth), s_Metal);

            foreach (var end in new[] { xMin - 0.02f, xMax + 0.02f })
            {
                var door = Box("End Door", root, new Vector3(end, RackHeight * 0.5f, centre.z), new Vector3(0.04f, RackHeight, depth), s_Containment, true);
                door.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
                Box("Door Frame", root, new Vector3(end, RackHeight * 0.5f, centre.z), new Vector3(0.05f, RackHeight, 0.05f), s_Metal);
                Box("Door Sign", root, new Vector3(end + (end < centre.x ? -0.026f : 0.026f), 1.6f, centre.z), new Vector3(0.002f, 0.12f, 0.35f), s_Hazard);
            }
        }

        static void TrayRun(Transform parent, RowSpec row)
        {
            var length = row.count * RackWidth;
            var centreX = row.start.x + length * 0.5f;
            var front = row.start.z + (row.facesNorth ? 0.35f : -0.35f);
            var back = row.start.z + (row.facesNorth ? -0.3f : 0.3f);

            Box($"Fiber Duct {row.name}", parent, new Vector3(centreX, 2.45f, front), new Vector3(length, 0.1f, 0.3f), s_TrayYellow);
            Box($"Basket {row.name}", parent, new Vector3(centreX, 2.75f, back), new Vector3(length, 0.06f, 0.45f), s_TrayGrey);

            for (var x = row.start.x + 0.3f; x < row.start.x + length; x += 1.8f)
            {
                Box("Rod", parent, new Vector3(x, (2.45f + CeilingHeight) * 0.5f, front), new Vector3(0.012f, CeilingHeight - 2.45f, 0.012f), s_Metal);
                Box("Rod", parent, new Vector3(x, (2.75f + CeilingHeight) * 0.5f, back), new Vector3(0.012f, CeilingHeight - 2.75f, 0.012f), s_Metal);
            }
        }

        static void RowEndLabel(Transform parent, RowSpec row, Color accent)
        {
            var end = row.start + new Vector3(-0.02f, 0f, 0f);
            var label = Child($"Row Label {row.name}", parent, end + new Vector3(0f, 2.15f, 0f));
            // Readable from the cross aisle west of the rows.
            label.rotation = Quaternion.Euler(0f, 90f, 0f);
            Box("Plate", label, Vector3.zero, new Vector3(0.5f, 0.18f, 0.01f), s_SignBack);
            Text("Text", label, new Vector3(0f, 0f, -0.007f), $"ROW {row.name}   R{row.firstNumber:00}-R{row.firstNumber + row.count - 1:00}", 0.55f, accent, new Vector2(0.48f, 0.16f));
        }

        public static void HangingSign(Transform parent, string title, string subtitle, Vector3 position, Quaternion rotation, Color accent)
        {
            var sign = Child($"Sign {title}", parent, position);
            sign.rotation = rotation;
            Box("Board", sign, Vector3.zero, new Vector3(2.2f, 0.5f, 0.03f), s_SignBack);
            Box("Accent", sign, new Vector3(0f, -0.235f, -0.016f), new Vector3(2.2f, 0.03f, 0.002f), BuilderAssets.UnlitMaterial($"Accent_{ColorUtility.ToHtmlStringRGB(accent)}", accent));
            Box("Wire L", sign, new Vector3(-0.9f, (CeilingHeight - position.y) * 0.5f + 0.25f, 0f), new Vector3(0.006f, CeilingHeight - position.y - 0.25f, 0.006f), s_Metal);
            Box("Wire R", sign, new Vector3(0.9f, (CeilingHeight - position.y) * 0.5f + 0.25f, 0f), new Vector3(0.006f, CeilingHeight - position.y - 0.25f, 0.006f), s_Metal);
            Text("Title", sign, new Vector3(0f, 0.07f, -0.017f), title, 2.2f, Color.white, new Vector2(2.1f, 0.3f));
            Text("Subtitle", sign, new Vector3(0f, -0.13f, -0.017f), subtitle, 1.0f, new Color(0.7f, 0.75f, 0.8f), new Vector2(2.1f, 0.14f));
        }

        public static TextMeshPro Text(string name, Transform parent, Vector3 localPosition, string text, float size, Color color, Vector2 rect)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = rect;
            tmp.fontStyle = FontStyles.Bold;
            return tmp;
        }

        // ---------------------------------------------------------------- Work rack

        /// <summary>
        /// The rack the robot patches: an open-frame rack (no door) with a 24-port RJ45 patch panel
        /// at U38 and a 24-port top-of-rack switch at U40, every port a live <see cref="PortSocket"/>.
        /// </summary>
        static Transform BuildWorkRack(Transform parent, string rackId, Vector3 position, Quaternion rotation, PortType rj45)
        {
            var rack = new GameObject(rackId).transform;
            rack.SetParent(parent, false);
            rack.SetPositionAndRotation(position, rotation);

            var collider = rack.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, RackHeight * 0.5f, -0.04f);
            collider.size = new Vector3(RackWidth, RackHeight, RackDepth - 0.08f);
            rack.gameObject.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);

            // Open frame: sides, back, roof and four posts. No door, so ports are reachable.
            var frame = Child("Frame", rack, Vector3.zero);
            Box("Side L", frame, new Vector3(-RackWidth * 0.5f + 0.01f, RackHeight * 0.5f, 0f), new Vector3(0.02f, RackHeight, RackDepth), s_RackBody);
            Box("Side R", frame, new Vector3(RackWidth * 0.5f - 0.01f, RackHeight * 0.5f, 0f), new Vector3(0.02f, RackHeight, RackDepth), s_RackBody);
            Box("Back", frame, new Vector3(0f, RackHeight * 0.5f, -RackDepth * 0.5f + 0.01f), new Vector3(RackWidth, RackHeight, 0.02f), s_RackBody);
            Box("Roof", frame, new Vector3(0f, RackHeight - 0.01f, 0f), new Vector3(RackWidth, 0.02f, RackDepth), s_RackBody);
            Box("Plinth", frame, new Vector3(0f, 0.025f, 0f), new Vector3(RackWidth + 0.01f, 0.05f, RackDepth + 0.01f), s_Dark);
            foreach (var x in new[] { -0.255f, 0.255f })
                Box("Rail", frame, new Vector3(x, RackHeight * 0.5f, PanelFace + 0.01f), new Vector3(0.02f, RackHeight - 0.1f, 0.02f), s_Metal);

            // Servers filling the rest, so the open rack does not look empty.
            var servers = Child("Servers", rack, Vector3.zero);
            var random = new System.Random(rackId.GetHashCode());
            for (var unit = 1; unit <= 36;)
            {
                var height = random.NextDouble() < 0.4 ? 2 : 1;
                if (random.NextDouble() < 0.2)
                {
                    unit += height;
                    continue;
                }

                var y = UnitBottom + (unit - 1) * UnitHeight + height * UnitHeight * 0.5f;
                Box($"Server U{unit}", servers, new Vector3(0f, y, PanelFace - 0.38f), new Vector3(0.44f, height * UnitHeight - 0.002f, 0.76f), s_SwitchBody);
                Box("Bezel", servers, new Vector3(0f, y, PanelFace - 0.0f), new Vector3(0.48f, height * UnitHeight - 0.003f, 0.008f), s_Dark);
                unit += height;
            }

            Leds("Server LEDs", servers, BuilderAssets.RackLedMesh(9), new Vector3(0f, 0f, PanelFace + 0.0045f));

            BuildPatchPanel(rack, rackId, 38, rj45);
            BuildSwitch(rack, rackId, 40, rj45);

            // Rack id plate on the roof edge.
            var plate = Child("Id Plate", rack, new Vector3(0f, RackHeight - 0.06f, RackDepth * 0.5f + 0.005f));
            Box("Plate", plate, Vector3.zero, new Vector3(0.3f, 0.07f, 0.005f), s_White);
            Text("Id", plate, new Vector3(0f, 0f, 0.004f), rackId, 0.45f, new Color(0.08f, 0.1f, 0.12f), new Vector2(0.28f, 0.06f)).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            return rack;
        }

        /// <summary>Y of the centre of rack unit <paramref name="unit"/> (1-based).</summary>
        public static float UnitCentre(int unit) => UnitBottom + (unit - 1) * UnitHeight + UnitHeight * 0.5f;

        static void BuildPatchPanel(Transform rack, string rackId, int unit, PortType type)
        {
            var y = UnitCentre(unit);
            var panel = Child($"Patch Panel U{unit}", rack, new Vector3(0f, y, PanelFace));
            Box("Face", panel, new Vector3(0f, 0f, -0.002f), new Vector3(0.4826f, UnitHeight - 0.002f, 0.004f), s_PanelGrey);
            Box("Body", panel, new Vector3(0f, 0f, -0.06f), new Vector3(0.44f, UnitHeight - 0.004f, 0.11f), s_Dark);
            Ports(panel, rackId, unit, type, 24);
        }

        static void BuildSwitch(Transform rack, string rackId, int unit, PortType type)
        {
            var y = UnitCentre(unit);
            var device = Child($"ToR Switch U{unit}", rack, new Vector3(0f, y, PanelFace));
            Box("Face", device, new Vector3(0f, 0f, -0.002f), new Vector3(0.4826f, UnitHeight - 0.002f, 0.004f), s_SwitchBody);
            Box("Body", device, new Vector3(0f, 0f, -0.21f), new Vector3(0.44f, UnitHeight - 0.004f, 0.41f), s_SwitchBody);
            Ports(device, rackId, unit, type, 24);
        }

        /// <summary>
        /// Lays out a row of live ports. Port 1 is on the operator's left when facing the rack, as
        /// on real panels, which is +X in rack space.
        /// </summary>
        static void Ports(Transform device, string rackId, int unit, PortType type, int count)
        {
            const float pitch = 0.0185f;
            var first = (count - 1) * pitch * 0.5f;
            var portLayer = BuilderAssets.Layer(BuilderAssets.PortLayer);

            for (var i = 0; i < count; i++)
            {
                var number = i + 1;
                var port = Child($"P{number:00}", device, new Vector3(first - i * pitch, -0.003f, 0f));

                // Port forward points into the device: the insertion direction.
                port.localRotation = Quaternion.Euler(0f, 180f, 0f);
                port.gameObject.layer = portLayer;

                var hole = Box("Opening", port, new Vector3(0f, 0f, -0.0005f), new Vector3(0.0135f, 0.0115f, 0.003f), s_PortHole);
                hole.layer = portLayer;
                var led = Box("LED", port, new Vector3(0f, 0.0105f, -0.0012f), new Vector3(0.004f, 0.0025f, 0.001f), s_PortLed);
                led.layer = portLayer;
                var ledRenderer = led.GetComponent<MeshRenderer>();
                ledRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var socket = Child("Socket", port, new Vector3(0f, 0f, 0.012f));

                var trigger = port.gameObject.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 0f, 0.004f);
                trigger.size = new Vector3(0.016f, 0.014f, 0.03f);

                var portSocket = port.gameObject.AddComponent<PortSocket>();
                portSocket.Configure($"{rackId}-U{unit}-P{number:00}", type, socket, ledRenderer);
            }
        }

        // ---------------------------------------------------------------- Cart, connectors, cables

        public static Transform BuildCart(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var cart = new GameObject(name).transform;
            cart.SetParent(parent, false);
            cart.SetPositionAndRotation(position, rotation);
            cart.gameObject.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);

            const float top = 0.95f;
            Box("Top", cart, new Vector3(0f, top - 0.015f, 0f), new Vector3(0.42f, 0.03f, 0.62f), s_White);
            Box("Lip", cart, new Vector3(0f, top + 0.01f, 0.3f), new Vector3(0.42f, 0.02f, 0.02f), s_Metal);
            Box("Lip", cart, new Vector3(0f, top + 0.01f, -0.3f), new Vector3(0.42f, 0.02f, 0.02f), s_Metal);
            Box("Shelf", cart, new Vector3(0f, 0.25f, 0f), new Vector3(0.4f, 0.02f, 0.6f), s_Metal);
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                Box("Leg", cart, new Vector3(corner.x * 0.19f, top * 0.5f, corner.y * 0.29f), new Vector3(0.025f, top - 0.04f, 0.025f), s_Metal);
                Box("Caster", cart, new Vector3(corner.x * 0.19f, 0.03f, corner.y * 0.29f), new Vector3(0.03f, 0.06f, 0.05f), s_Dark);
            }

            var collider = cart.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, top * 0.5f, 0f);
            collider.size = new Vector3(0.42f, top, 0.62f);
            return cart;
        }

        /// <summary>Height of the cart top surface, for placing things on it.</summary>
        public const float CartTop = 0.95f;

        public static GameObject BuildConnectorPrefab(PortType type)
        {
            var root = new GameObject($"Connector_{type.code}");
            var cableLayer = BuilderAssets.Layer(BuilderAssets.CableLayer);
            var jacket = BuilderAssets.LitMaterial($"Cable_{type.code}", type.cableColor, 0f, 0.35f);
            var plug = BuilderAssets.LitMaterial($"Plug_{type.code}", type.code == "C13" ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.82f, 0.85f, 0.88f), 0.1f, 0.75f);

            Vector3 bodySize, bootSize;
            switch (type.code)
            {
                case "LC":
                    bodySize = new Vector3(0.0125f, 0.0095f, 0.028f);
                    bootSize = new Vector3(0.012f, 0.011f, 0.03f);
                    break;
                case "C13":
                    bodySize = new Vector3(0.03f, 0.022f, 0.035f);
                    bootSize = new Vector3(0.022f, 0.02f, 0.03f);
                    break;
                default:
                    bodySize = new Vector3(0.0118f, 0.0088f, 0.022f);
                    bootSize = new Vector3(0.014f, 0.012f, 0.02f);
                    break;
            }

            // Origin at the body centre; tip at the front face; cable leaves the rear of the boot.
            Box("Body", root.transform, Vector3.zero, bodySize, plug);
            Box("Boot", root.transform, new Vector3(0f, 0f, -(bodySize.z + bootSize.z) * 0.5f), bootSize, jacket);
            if (type.code == "RJ45")
                Box("Latch", root.transform, new Vector3(0f, bodySize.y * 0.5f + 0.001f, -0.002f), new Vector3(0.005f, 0.002f, 0.014f), plug);

            var tip = Child("Tip", root.transform, new Vector3(0f, 0f, bodySize.z * 0.5f));
            var anchor = Child("Cable Anchor", root.transform, new Vector3(0f, 0f, -(bodySize.z * 0.5f + bootSize.z)));

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0f, -bootSize.z * 0.5f);
            collider.size = new Vector3(Mathf.Max(bodySize.x, bootSize.x) + 0.006f, Mathf.Max(bodySize.y, bootSize.y) + 0.004f, bodySize.z + bootSize.z);

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.03f;
            body.linearDamping = 1f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var connector = root.AddComponent<CableConnector>();
            connector.Configure(type, tip, anchor, BuilderAssets.Mask(BuilderAssets.PortLayer), type.code);

            SetLayerRecursively(root, cableLayer);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{BuilderAssets.PrefabsFolder}/Connector_{type.code}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Places a cable: two connectors from <paramref name="connectorPrefab"/> and a
        /// <see cref="VerletCable"/> between them.
        /// </summary>
        public static VerletCable PlaceCable(Transform parent, string cableId, GameObject connectorPrefab, PortType type,
            Pose endA, Pose endB, float length)
        {
            var root = new GameObject($"Cable {cableId}").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = BuilderAssets.Layer(BuilderAssets.CableLayer);

            var a = (GameObject)PrefabUtility.InstantiatePrefab(connectorPrefab, root);
            a.name = $"{cableId} End A";
            a.transform.SetPositionAndRotation(endA.position, endA.rotation);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(connectorPrefab, root);
            b.name = $"{cableId} End B";
            b.transform.SetPositionAndRotation(endB.position, endB.rotation);

            var connectorA = a.GetComponent<CableConnector>();
            var connectorB = b.GetComponent<CableConnector>();
            connectorA.Configure(type, connectorA.tip, connectorA.cableAnchor, BuilderAssets.Mask(BuilderAssets.PortLayer), $"{cableId} A");
            connectorB.Configure(type, connectorB.tip, connectorB.cableAnchor, BuilderAssets.Mask(BuilderAssets.PortLayer), $"{cableId} B");
            PrefabUtility.RecordPrefabInstancePropertyModifications(connectorA);
            PrefabUtility.RecordPrefabInstancePropertyModifications(connectorB);

            var line = root.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = BuilderAssets.LitMaterial($"Cable_{type.code}", type.cableColor, 0f, 0.35f);
            line.widthMultiplier = type.cableDiameter;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.generateLightingData = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.textureMode = LineTextureMode.Stretch;

            var cable = root.gameObject.AddComponent<VerletCable>();
            cable.Configure(cableId, connectorA, connectorB, length, 0f);
            return cable;
        }

        // ---------------------------------------------------------------- Source model

        /// <summary>
        /// Places the supplied data center equipment model (three racks, servers, monitor, cabling)
        /// as a showcase bay, wrapped in an LOD group with a box proxy so its 134k triangles only
        /// render when the camera is close.
        /// </summary>
        public static Transform PlaceSourceModel(Transform parent, Vector3 position, float yaw)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BuilderAssets.SourceModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Data Center] Source model not found at {BuilderAssets.SourceModelPath}; skipping showcase bay.");
                return null;
            }

            var bay = new GameObject("Showcase Bay (source model)").transform;
            bay.SetParent(parent, false);
            bay.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, bay);
            instance.transform.localPosition = Vector3.zero;

            // Sit it on the floor and centre it on the bay origin, whatever the FBX pivot is.
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return bay;

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            instance.transform.position += new Vector3(position.x - bounds.center.x, position.y - bounds.min.y, position.z - bounds.center.z);

            // Recompute after the move for the proxy and collider.
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            // Far LOD: one box per rack and chassis. Cables, keyboard and screens simply disappear.
            var proxyRoot = Child("LOD1 Proxy", bay, Vector3.zero);
            var proxies = new List<Renderer>();
            foreach (var renderer in renderers)
            {
                var name = renderer.name.ToUpperInvariant();
                if (!name.StartsWith("RACK") && !name.StartsWith("CHASSIS"))
                    continue;

                var b = renderer.bounds;
                var proxy = Box($"Proxy {renderer.name}", proxyRoot, bay.InverseTransformPoint(b.center), b.size, s_RackBody);
                proxies.Add(proxy.GetComponent<Renderer>());
            }

            var group = bay.gameObject.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                // The bay is 5 m across, so even 0.7 of screen height is only reached within about
                // 7 m. Beyond that the 134k-triangle model drops to its proxies.
                new LOD(0.7f, renderers),
                new LOD(0.01f, proxies.ToArray()),
            });
            group.RecalculateBounds();

            // Collide with the racks only, not the floor cable spread around them.
            var rackBounds = new Bounds(bounds.center, Vector3.zero);
            var first = true;
            foreach (var proxy in proxies)
            {
                if (first)
                {
                    rackBounds = proxy.bounds;
                    first = false;
                }
                else
                {
                    rackBounds.Encapsulate(proxy.bounds);
                }
            }

            var collider = bay.gameObject.AddComponent<BoxCollider>();
            collider.center = bay.InverseTransformPoint(rackBounds.center);
            collider.size = rackBounds.size;

            SetLayerRecursively(bay.gameObject, BuilderAssets.Layer(BuilderAssets.EquipmentLayer));
            return bay;
        }
    }
}
