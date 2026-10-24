using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using DataCenter.Tasks;
using static DataCenter.Editor.RobotBuilder;

namespace DataCenter.Editor
{
    /// <summary>One device in a work rack: a patch panel, switch, PDU or server with live ports.</summary>
    struct DeviceSpec
    {
        public string name;
        public int unit;
        public int units;
        public PortType type;
        public int ports;
        public float pitch;
        public Vector2 opening;
        public Material face;
        public float depth;
    }

    /// <summary>Where a section's robot parks and how its cart and cables are laid out around it.</summary>
    struct StationSpec
    {
        public string robotId;
        public string robotName;
        public string sectionId;
        public Vector3 spawn;
        public float yaw;
    }

    /// <summary>Sections B, C and D, the second block of Section A, and the shared station layout.</summary>
    static partial class EnvironmentBuilder
    {
        static Material s_Wood, s_Orange, s_Busway, s_Ups, s_Breaker, s_Chassis, s_Fiber, s_DoorSlats, s_Pad, s_SpoolCore;

        static void CreateSectionMaterials()
        {
            s_Wood = BuilderAssets.LitMaterial("Crate_Wood", new Color(0.62f, 0.47f, 0.3f), 0f, 0.2f);
            s_Orange = BuilderAssets.LitMaterial("Racking_Orange", new Color(0.95f, 0.45f, 0.08f), 0.3f, 0.45f);
            s_Busway = BuilderAssets.LitMaterial("Busway", new Color(0.85f, 0.42f, 0.12f), 0.5f, 0.5f);
            s_Ups = BuilderAssets.LitMaterial("UPS_Cabinet", new Color(0.82f, 0.84f, 0.86f), 0.2f, 0.4f);
            s_Breaker = BuilderAssets.LitMaterial("Breaker_Panel", new Color(0.32f, 0.35f, 0.38f), 0.4f, 0.45f);
            s_Chassis = BuilderAssets.LitMaterial("Core_Chassis", new Color(0.12f, 0.15f, 0.2f), 0.5f, 0.5f);
            s_Fiber = BuilderAssets.LitMaterial("Fiber_Jumper", new Color(1f, 0.82f, 0.05f), 0f, 0.4f);
            s_DoorSlats = BuilderAssets.LitMaterial("Rollup_Door", new Color(0.66f, 0.69f, 0.72f), 0.6f, 0.4f);
            s_Pad = BuilderAssets.LedMaterial("Charge_Pad", new Color(0.18f, 0.9f, 0.42f), false, 1.2f);
            s_SpoolCore = BuilderAssets.LitMaterial("Spool", new Color(0.55f, 0.4f, 0.25f), 0f, 0.3f);
        }

        // ---------------------------------------------------------------- Stations

        public static readonly StationSpec StationA = new StationSpec { robotId = "R-A1", robotName = "Atlas", sectionId = "A", spawn = new Vector3(-13.5f, 0f, 4.0f), yaw = 180f };
        public static readonly StationSpec StationB = new StationSpec { robotId = "R-B1", robotName = "Borealis", sectionId = "B", spawn = new Vector3(6.5f, 0f, 4.0f), yaw = 180f };
        public static readonly StationSpec StationC = new StationSpec { robotId = "R-C1", robotName = "Cobalt", sectionId = "C", spawn = new Vector3(5.9f, 0f, -4.4f), yaw = 0f };
        public static readonly StationSpec StationD = new StationSpec { robotId = "R-D1", robotName = "Dash", sectionId = "D", spawn = new Vector3(-8f, 0f, -5.15f), yaw = 0f };

        /// <summary>World pose of a point given in a station's robot frame (x right, z forward).</summary>
        public static Vector3 StationPoint(StationSpec station, float right, float up, float forward)
        {
            var rotation = Quaternion.Euler(0f, station.yaw, 0f);
            return station.spawn + rotation * new Vector3(right, up, forward);
        }

        /// <summary>
        /// Slots on the cart, in the robot frame, on the cart edge nearest the arm so every slot is
        /// inside the arm's 0.84 m reach with the tool pointing down.
        /// </summary>
        static readonly Vector2[] k_CartSlots =
        {
            new Vector2(-0.64f, 0.24f), new Vector2(-0.64f, 0.12f),
            new Vector2(-0.64f, 0.00f), new Vector2(-0.64f, -0.12f),
            new Vector2(-0.76f, 0.20f), new Vector2(-0.76f, 0.08f),
        };

        /// <summary>Places the cable cart on the robot's left and lays cables on it, two slots per cable.</summary>
        public static void BuildStationCart(Transform parent, Transform cables, StationSpec station, params (string id, GameObject prefab, PortType type, float length)[] leads)
        {
            var rotation = Quaternion.Euler(0f, station.yaw, 0f);
            BuildCart(parent, $"Cable Cart {station.sectionId}", StationPoint(station, -0.82f, 0f, 0f), rotation);

            var top = CartTop + 0.008f;
            for (var i = 0; i < leads.Length && i * 2 + 1 < k_CartSlots.Length; i++)
            {
                var a = k_CartSlots[i * 2];
                var b = k_CartSlots[i * 2 + 1];
                PlaceCable(cables, leads[i].id, leads[i].prefab, leads[i].type,
                    new Pose(StationPoint(station, a.x, top, a.y), rotation),
                    new Pose(StationPoint(station, b.x, top, b.y), rotation),
                    leads[i].length);
            }
        }

        // ---------------------------------------------------------------- Generic rack rows and work racks

        /// <summary>Builds rows of decorative racks, swapping in custom builders for special rack numbers.</summary>
        static void BuildRows(Transform parent, string sectionId, RowSpec[] rows, GameObject[] rackPrefabs, Color accent,
            Dictionary<int, System.Action<Transform, string, Vector3, Quaternion>> special, List<Rect> footprints)
        {
            var variant = (int)sectionId[0];
            foreach (var row in rows)
            {
                var rowRoot = Child($"Row {row.name}", parent, Vector3.zero);
                for (var i = 0; i < row.count; i++)
                {
                    var number = row.firstNumber + i;
                    var id = $"{sectionId}-R{number:00}";
                    var position = RackPosition(row, i);
                    var rotation = RackRotation(row);

                    if (special != null && special.TryGetValue(number, out var build))
                    {
                        build(rowRoot, id, position, rotation);
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(rackPrefabs[variant++ % rackPrefabs.Length], rowRoot);
                    instance.name = id;
                    instance.transform.SetPositionAndRotation(position, rotation);
                }

                RowEndLabel(rowRoot, row, accent);
                footprints.Add(Rect.MinMaxRect(row.start.x, row.start.z - RackDepth * 0.5f, row.start.x + row.count * RackWidth, row.start.z + RackDepth * 0.5f));
            }
        }

        /// <summary>An open-frame rack with live devices and filler servers. Devices face the rack front.</summary>
        static Transform BuildWorkRack(Transform parent, string rackId, Vector3 position, Quaternion rotation, params DeviceSpec[] devices)
        {
            var rack = new GameObject(rackId).transform;
            rack.SetParent(parent, false);
            rack.SetPositionAndRotation(position, rotation);
            rack.gameObject.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);

            var collider = rack.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, RackHeight * 0.5f, -0.04f);
            collider.size = new Vector3(RackWidth, RackHeight, RackDepth - 0.08f);

            var frame = Child("Frame", rack, Vector3.zero);
            Box("Side L", frame, new Vector3(-RackWidth * 0.5f + 0.01f, RackHeight * 0.5f, 0f), new Vector3(0.02f, RackHeight, RackDepth), s_RackBody);
            Box("Side R", frame, new Vector3(RackWidth * 0.5f - 0.01f, RackHeight * 0.5f, 0f), new Vector3(0.02f, RackHeight, RackDepth), s_RackBody);
            Box("Back", frame, new Vector3(0f, RackHeight * 0.5f, -RackDepth * 0.5f + 0.01f), new Vector3(RackWidth, RackHeight, 0.02f), s_RackBody);
            Box("Roof", frame, new Vector3(0f, RackHeight - 0.01f, 0f), new Vector3(RackWidth, 0.02f, RackDepth), s_RackBody);
            Box("Plinth", frame, new Vector3(0f, 0.025f, 0f), new Vector3(RackWidth + 0.01f, 0.05f, RackDepth + 0.01f), s_Dark);
            foreach (var x in new[] { -0.255f, 0.255f })
                Box("Rail", frame, new Vector3(x, RackHeight * 0.5f, PanelFace + 0.01f), new Vector3(0.02f, RackHeight - 0.1f, 0.02f), s_Metal);

            // Filler servers in the units the devices leave free.
            var used = new HashSet<int>();
            foreach (var device in devices)
            {
                for (var u = 0; u < Mathf.Max(1, device.units); u++)
                    used.Add(device.unit + u);
            }

            var servers = Child("Servers", rack, Vector3.zero);
            var random = new System.Random(rackId.GetHashCode());
            for (var unit = 1; unit <= 42;)
            {
                var height = random.NextDouble() < 0.4 ? 2 : 1;
                if (used.Contains(unit) || used.Contains(unit + height - 1) || random.NextDouble() < 0.25)
                {
                    unit++;
                    continue;
                }

                var y = UnitBottom + (unit - 1) * UnitHeight + height * UnitHeight * 0.5f;
                Box($"Server U{unit}", servers, new Vector3(0f, y, PanelFace - 0.38f), new Vector3(0.44f, height * UnitHeight - 0.002f, 0.76f), s_SwitchBody);
                Box("Bezel", servers, new Vector3(0f, y, PanelFace), new Vector3(0.48f, height * UnitHeight - 0.003f, 0.008f), s_Dark);
                unit += height;
            }

            Leds("Server LEDs", servers, BuilderAssets.RackLedMesh(9), new Vector3(0f, 0f, PanelFace + 0.0045f));

            foreach (var device in devices)
                BuildDevice(rack, rackId, device);

            var plate = Child("Id Plate", rack, new Vector3(0f, RackHeight - 0.06f, RackDepth * 0.5f + 0.005f));
            Box("Plate", plate, Vector3.zero, new Vector3(0.3f, 0.07f, 0.005f), s_White);
            Text("Id", plate, new Vector3(0f, 0f, 0.004f), rackId, 0.45f, new Color(0.08f, 0.1f, 0.12f), new Vector2(0.28f, 0.06f)).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            return rack;
        }

        static void BuildDevice(Transform rack, string rackId, DeviceSpec device)
        {
            var units = Mathf.Max(1, device.units);
            var height = units * UnitHeight;
            var y = UnitBottom + (device.unit - 1) * UnitHeight + height * 0.5f;
            var root = Child($"{device.name} U{device.unit}", rack, new Vector3(0f, y, PanelFace));
            Box("Face", root, new Vector3(0f, 0f, -0.002f), new Vector3(0.4826f, height - 0.002f, 0.004f), device.face);
            Box("Body", root, new Vector3(0f, 0f, -0.004f - device.depth * 0.5f), new Vector3(0.44f, height - 0.004f, device.depth), s_Dark);
            Ports(root, rackId, device.unit, device.type, device.ports, device.pitch, device.opening);
        }

        // ---------------------------------------------------------------- Section A, second block

        public static readonly RowSpec[] SectionARowsEast =
        {
            new RowSpec { name = "A5", start = new Vector3(-8f, 0f, 2.6f), facesNorth = true, count = 10, firstNumber = 41 },
            new RowSpec { name = "A6", start = new Vector3(-8f, 0f, 5.9f), facesNorth = false, count = 10, firstNumber = 51 },
            new RowSpec { name = "A7", start = new Vector3(-8f, 0f, 8.25f), facesNorth = true, count = 10, firstNumber = 61 },
            new RowSpec { name = "A8", start = new Vector3(-8f, 0f, 11.55f), facesNorth = false, count = 10, firstNumber = 71 },
        };

        // ---------------------------------------------------------------- Section B: Network core / MDF

        static readonly RowSpec[] k_SectionBRows =
        {
            new RowSpec { name = "B1", start = new Vector3(5f, 0f, 2.6f), facesNorth = true, count = 8, firstNumber = 1 },
            new RowSpec { name = "B2", start = new Vector3(5f, 0f, 5.9f), facesNorth = false, count = 8, firstNumber = 11 },
            new RowSpec { name = "B3", start = new Vector3(5f, 0f, 9.6f), facesNorth = false, count = 8, firstNumber = 21 },
        };

        public static void BuildSectionB(Transform parent, GameObject[] rackPrefabs, PortType lc, List<Rect> footprints)
        {
            var layout = Sections[1];
            var section = Child("Section B - Network Core", parent, Vector3.zero);
            AisleTiles(Child("Aisle Tiles", section, Vector3.zero), "Cold Aisle B1-B2", Rect.MinMaxRect(5f, 3.15f, 9.8f, 5.35f));

            var special = new Dictionary<int, System.Action<Transform, string, Vector3, Quaternion>>
            {
                [3] = (row, id, position, rotation) => BuildWorkRack(row, id, position, rotation,
                    new DeviceSpec { name = "Fiber Panel", unit = 30, units = 1, type = lc, ports = 24, pitch = 0.0185f, opening = new Vector2(0.0125f, 0.0095f), face = s_PanelGrey, depth = 0.12f },
                    new DeviceSpec { name = "Core Line Card", unit = 32, units = 2, type = lc, ports = 24, pitch = 0.0185f, opening = new Vector2(0.0125f, 0.0095f), face = s_Chassis, depth = 0.6f }),
                [14] = CoreChassis,
                [15] = CoreChassis,
            };
            BuildRows(Child("Racks", section, Vector3.zero), "B", k_SectionBRows, rackPrefabs, layout.accent, special, footprints);

            // Row B3 is the fiber distribution frame: open frames dense with yellow jumpers.
            var odf = Child("ODF Jumpers", section, Vector3.zero);
            var random = new System.Random(77);
            for (var i = 0; i < 40; i++)
            {
                var x = 5.1f + (float)random.NextDouble() * 4.6f;
                var drop = 0.5f + (float)random.NextDouble() * 1.2f;
                Box("Jumper", odf, new Vector3(x, 2.0f - drop * 0.5f, 9.6f - 0.57f), new Vector3(0.004f, drop, 0.004f), s_Fiber);
            }

            HotAisle(Child("Hot Aisle Containment", section, Vector3.zero), "B1", 5f, 9.8f, 1.0f, 2.05f);
            var trays = Child("Cable Trays", section, Vector3.zero);
            foreach (var row in k_SectionBRows)
                TrayRun(trays, row);

            // MDF demarcation: carrier entrance boxes on the east wall.
            var demarc = Child("Carrier Demarc", section, Vector3.zero);
            for (var i = 0; i < 4; i++)
                Box($"Carrier Box {i + 1}", demarc, new Vector3(HallHalfX - 0.15f, 1.4f, 3f + i * 1.2f), new Vector3(0.12f, 0.8f, 0.6f), s_Breaker);

            HangingSign(Child("Signage", section, Vector3.zero), "B  ·  NETWORK CORE", "MDF  ·  LC duplex fiber", new Vector3(10f, 3.1f, CorridorHalfWidth + 0.05f), Quaternion.identity, layout.accent);
        }

        static void CoreChassis(Transform row, string id, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject(id).transform;
            root.SetParent(row, false);
            root.SetPositionAndRotation(position, rotation);
            root.gameObject.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, RackHeight * 0.5f, 0f);
            collider.size = new Vector3(RackWidth, RackHeight, RackDepth);

            Box("Chassis", root, new Vector3(0f, RackHeight * 0.5f, 0f), new Vector3(RackWidth, RackHeight, RackDepth), s_Chassis);
            for (var slot = 0; slot < 12; slot++)
            {
                var y = 0.25f + slot * 0.13f;
                Box($"Line Card {slot + 1}", root, new Vector3(0f, y, RackDepth * 0.5f + 0.004f), new Vector3(0.52f, 0.11f, 0.008f), s_SwitchBody);
            }

            Leds("Chassis LEDs", root, BuilderAssets.RackLedMesh(20 + id.Length), new Vector3(0f, 0f, RackDepth * 0.5f + 0.0095f));
            Box("Logo Strip", root, new Vector3(0f, 1.92f, RackDepth * 0.5f + 0.004f), new Vector3(0.5f, 0.04f, 0.008f), BuilderAssets.UnlitMaterial("Accent_Core", Sections[1].accent));
        }

        // ---------------------------------------------------------------- Section C: Power room

        static readonly RowSpec[] k_SectionCRows =
        {
            new RowSpec { name = "C1", start = new Vector3(5f, 0f, -3.0f), facesNorth = false, count = 7, firstNumber = 1 },
        };

        public static void BuildSectionC(Transform parent, GameObject[] rackPrefabs, PortType c13, List<Rect> footprints)
        {
            var layout = Sections[2];
            var section = Child("Section C - Power Room", parent, Vector3.zero);

            var special = new Dictionary<int, System.Action<Transform, string, Vector3, Quaternion>>
            {
                [2] = (row, id, position, rotation) => BuildWorkRack(row, id, position, rotation,
                    new DeviceSpec { name = "PDU-A", unit = 20, units = 1, type = c13, ports = 10, pitch = 0.042f, opening = new Vector2(0.026f, 0.02f), face = s_Breaker, depth = 0.15f },
                    new DeviceSpec { name = "Server PSU", unit = 24, units = 2, type = c13, ports = 2, pitch = 0.12f, opening = new Vector2(0.026f, 0.02f), face = s_SwitchBody, depth = 0.7f }),
            };
            BuildRows(Child("Racks", section, Vector3.zero), "C", k_SectionCRows, rackPrefabs, layout.accent, special, footprints);

            // UPS and battery line-up along the south wall.
            var ups = Child("UPS Line-up", section, Vector3.zero);
            for (var i = 0; i < 12; i++)
            {
                var battery = i >= 6;
                var x = 4.2f + i * 1.25f;
                var cabinet = Box(battery ? $"Battery Cabinet {i - 5}" : $"UPS Module {i + 1}", ups, new Vector3(x, 1.0f, -11.65f), new Vector3(1.2f, 2.0f, 0.9f), battery ? s_RackBody : s_Ups, true);
                cabinet.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
                Box("Display", ups, new Vector3(x - 0.3f, 1.5f, -11.65f + 0.452f), new Vector3(0.2f, 0.12f, 0.004f), battery ? s_PortHole : BuilderAssets.UnlitMaterial("UPS_Display", new Color(0.2f, 0.9f, 0.5f)));
                Box("Vent", ups, new Vector3(x, 0.4f, -11.65f + 0.452f), new Vector3(1.0f, 0.3f, 0.004f), s_Breaker);
            }

            footprints.Add(Rect.MinMaxRect(3.6f, -12.1f, 19.3f, -11.2f));
            Box("UPS Hazard Tape", ups, new Vector3(11.5f, 0.002f, -10.9f), new Vector3(15.6f, 0.002f, 0.1f), s_Hazard);

            // Floor-standing PDUs.
            var pdus = Child("Floor PDUs", section, Vector3.zero);
            for (var i = 0; i < 3; i++)
            {
                var x = 11f + i * 1.6f;
                var pdu = Box($"PDU Cabinet {i + 1}", pdus, new Vector3(x, 0.95f, -6.5f), new Vector3(0.8f, 1.9f, 0.8f), s_Ups, true);
                pdu.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
                Box("Breakers", pdus, new Vector3(x, 1.1f, -6.5f + 0.402f), new Vector3(0.6f, 0.9f, 0.004f), s_Breaker);
                footprints.Add(Rect.MinMaxRect(x - 0.4f, -6.9f, x + 0.4f, -6.1f));
            }

            // Overhead busway above the rack row.
            Box("Busway", Child("Busway", section, Vector3.zero), new Vector3(7.1f, 2.6f, -3.3f), new Vector3(4.6f, 0.15f, 0.2f), s_Busway);

            var signs = Child("Signage", section, Vector3.zero);
            HangingSign(signs, "C  ·  POWER ROOM", "UPS · PDU · C13/C14 power", new Vector3(10f, 3.1f, -CorridorHalfWidth - 0.05f), Quaternion.Euler(0f, 180f, 0f), layout.accent);
            WallSign(signs, "DANGER  480 V", new Vector3(12f, 2.4f, -HallHalfZ + 0.02f), Quaternion.identity);
        }

        static void WallSign(Transform parent, string text, Vector3 position, Quaternion rotation)
        {
            var sign = Child($"Wall Sign {text}", parent, position);
            sign.rotation = rotation;
            Box("Plate", sign, Vector3.zero, new Vector3(1.4f, 0.35f, 0.01f), BuilderAssets.LitMaterial("Sign_Yellow", new Color(1f, 0.8f, 0.1f), 0f, 0.4f));
            Text("Text", sign, new Vector3(0f, 0f, -0.007f), text, 1.6f, new Color(0.08f, 0.08f, 0.08f), new Vector2(1.3f, 0.3f));
        }

        // ---------------------------------------------------------------- Section D: Staging / loading dock

        public static void BuildSectionD(Transform parent, PortType rj45, List<Rect> footprints)
        {
            var layout = Sections[3];
            var section = Child("Section D - Staging", parent, Vector3.zero);

            // Training panel for the tutorial: a work rack with a six-port patch strip at a comfortable height.
            BuildWorkRack(section, "D-T01", new Vector3(StationD.spawn.x, 0f, -3.8f), Quaternion.Euler(0f, 180f, 0f),
                new DeviceSpec { name = "Training Panel", unit = 20, units = 2, type = rj45, ports = 6, pitch = 0.05f, opening = new Vector2(0.0135f, 0.0115f), face = BuilderAssets.UnlitMaterial("Training_Face", new Color(0.12f, 0.35f, 0.22f)), depth = 0.1f });
            footprints.Add(Rect.MinMaxRect(StationD.spawn.x - 0.3f, -4.35f, StationD.spawn.x + 0.3f, -3.25f));

            // Tutorial bay marked out on the floor.
            var bay = Child("Tutorial Bay", section, Vector3.zero);
            var centre = new Vector3(StationD.spawn.x, 0f, -5f);
            foreach (var edge in new[] { (new Vector3(0f, 0f, 1.6f), new Vector3(4f, 0.002f, 0.08f)), (new Vector3(0f, 0f, -1.6f), new Vector3(4f, 0.002f, 0.08f)),
                                         (new Vector3(2f, 0f, 0f), new Vector3(0.08f, 0.002f, 3.2f)), (new Vector3(-2f, 0f, 0f), new Vector3(0.08f, 0.002f, 3.2f)) })
                Box("Tape", bay, centre + edge.Item1 + Vector3.up * 0.002f, edge.Item2, BuilderAssets.UnlitMaterial("Accent_Tutorial", layout.accent));
            FloorText(bay, "TRAINING", centre + new Vector3(0f, 0.004f, -1.25f), 3f, layout.accent);

            // Charging pads for the fleet.
            var pads = Child("Charging Pads", section, Vector3.zero);
            for (var i = 0; i < 4; i++)
            {
                var p = new Vector3(-13f - i * 1.4f, 0.004f, -2.1f);
                Box("Pad Ring", pads, p, new Vector3(1.1f, 0.004f, 1.1f), s_Pad);
                Box("Pad Inner", pads, p + Vector3.up * 0.001f, new Vector3(0.95f, 0.004f, 0.95f), s_Dark);
                FloorText(pads, "CHARGE", p + Vector3.up * 0.004f, 1.6f, layout.accent);
            }

            // Pallet racking along the west wall with boxed equipment.
            var racking = Child("Pallet Racking", section, Vector3.zero);
            for (var bayIndex = 0; bayIndex < 4; bayIndex++)
            {
                var z = -11.5f + bayIndex * 2.4f;
                foreach (var dz in new[] { -1.15f, 1.15f })
                    Box("Upright", racking, new Vector3(-19.2f, 1.6f, z + dz), new Vector3(0.08f, 3.2f, 0.08f), s_Orange);
                foreach (var y in new[] { 0.15f, 1.3f, 2.45f })
                {
                    Box("Beam", racking, new Vector3(-19.2f, y, z), new Vector3(0.9f, 0.08f, 2.3f), s_Orange);
                    Box("Crate", racking, new Vector3(-19.2f, y + 0.36f, z - 0.5f), new Vector3(0.8f, 0.6f, 1.0f), s_Wood);
                    Box("Crate", racking, new Vector3(-19.2f, y + 0.31f, z + 0.6f), new Vector3(0.7f, 0.5f, 0.8f), s_Wood);
                }
            }

            footprints.Add(Rect.MinMaxRect(-19.7f, -12.2f, -18.7f, -2.6f));

            // Cable spools waiting to be pulled.
            var spools = Child("Cable Spools", section, Vector3.zero);
            for (var i = 0; i < 4; i++)
            {
                var p = new Vector3(-16.5f + i * 1.1f, 0.45f, -11.3f);
                Cylinder($"Spool {i + 1}", spools, p, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.9f, 0.18f, 0.9f), s_SpoolCore);
                Cylinder("Cable Wrap", spools, p, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.7f, 0.17f, 0.7f), i % 2 == 0 ? BuilderAssets.LitMaterial("Cable_RJ45", rj45.cableColor, 0f, 0.35f) : s_Fiber);
            }

            footprints.Add(Rect.MinMaxRect(-17f, -11.8f, -12.6f, -10.8f));

            // Pallets of crates near the loading door.
            var pallets = Child("Pallets", section, Vector3.zero);
            for (var i = 0; i < 3; i++)
            {
                var p = new Vector3(-6f + i * 1.5f, 0f, -10.6f);
                Box("Pallet", pallets, p + Vector3.up * 0.07f, new Vector3(1.2f, 0.14f, 1.0f), s_Wood, true).layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
                Box("Crate", pallets, p + Vector3.up * 0.55f, new Vector3(1.0f, 0.8f, 0.8f), s_Wood);
                footprints.Add(Rect.MinMaxRect(p.x - 0.6f, p.z - 0.5f, p.x + 0.6f, p.z + 0.5f));
            }

            // Roll-up loading door in the south wall.
            var door = Child("Loading Door", section, new Vector3(-10f, 0f, -HallHalfZ + 0.02f));
            Box("Door", door, new Vector3(0f, 1.6f, 0f), new Vector3(4f, 3.2f, 0.06f), s_DoorSlats);
            for (var y = 0.2f; y < 3.2f; y += 0.2f)
                Box("Slat", door, new Vector3(0f, y, 0.032f), new Vector3(4f, 0.01f, 0.005f), s_Metal);
            Box("Header", door, new Vector3(0f, 3.35f, 0.1f), new Vector3(4.3f, 0.3f, 0.25f), s_Breaker);
            foreach (var x in new[] { -2.4f, 2.4f })
                Cylinder("Bollard", door, new Vector3(x, 0.5f, 0.8f), Quaternion.identity, new Vector3(0.15f, 0.5f, 0.15f), s_Hazard);
            Box("Dock Tape", door, new Vector3(0f, 0.003f, 1.4f), new Vector3(4.4f, 0.002f, 0.1f), s_Hazard);

            HangingSign(Child("Signage", section, Vector3.zero), "D  ·  STAGING", "Loading dock  ·  Training bay", new Vector3(-10f, 3.1f, -CorridorHalfWidth - 0.05f), Quaternion.Euler(0f, 180f, 0f), layout.accent);
        }

        static void FloorText(Transform parent, string text, Vector3 position, float size, Color color)
        {
            var label = Text(text, parent, position, text, size, color, new Vector2(2.4f, 0.6f));
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // ---------------------------------------------------------------- Hall dressing shared by all sections

        /// <summary>Partition walls between east and west sections, and a big painted letter per section.</summary>
        public static void BuildSectionDividers(Transform parent)
        {
            var dividers = Child("Section Dividers", parent, Vector3.zero);
            var material = BuilderAssets.LitMaterial("Partition", new Color(0.55f, 0.6f, 0.66f), 0.1f, 0.6f);
            foreach (var (zMin, zMax) in new[] { (CorridorHalfWidth, HallHalfZ), (-HallHalfZ, -CorridorHalfWidth) })
            {
                var wall = Box("Partition", dividers, new Vector3(1f, 1.5f, (zMin + zMax) * 0.5f), new Vector3(0.1f, 3f, zMax - zMin), material, true);
                wall.layer = BuilderAssets.Layer(BuilderAssets.EquipmentLayer);
            }

            foreach (var section in Sections)
            {
                var corridorEdge = section.area.yMin > 0f ? section.area.yMin : section.area.yMax;
                var inset = section.area.yMin > 0f ? 0.06f : -0.06f;
                Box($"Edge {section.id}", dividers, new Vector3(section.area.center.x, 0.003f, corridorEdge + inset), new Vector3(section.area.width - 0.4f, 0.002f, 0.06f),
                    BuilderAssets.UnlitMaterial($"Accent_{ColorUtility.ToHtmlStringRGB(section.accent)}", section.accent));

                // Big painted section letter near the corridor, at the end nearest the hall's centre line.
                var letterX = section.area.center.x < 0f ? section.area.xMax - 2.5f : section.area.xMin + 2.5f;
                var letter = new Vector3(letterX, 0.004f, section.area.yMin > 0f ? corridorEdge + 1.6f : corridorEdge - 1.6f);
                var faded = section.accent;
                faded.a = 0.5f;
                FloorText(dividers, section.id, letter, 18f, faded);
            }
        }
    }
}
