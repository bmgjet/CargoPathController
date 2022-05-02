using ProtoBuf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

//Console Command
//createcargopath "newname.map" minwaterdepth mindistancefromprefabs smoothing		- Creates and saves new map file with cargopath of these settings embeded
//createcargopath "newname.map"														- Saves the currently loaded cargopath into a new mapfile
//stopcargopath																		- Stops the cargo path generator

//Chat Commands
// /createcargopath "newname.map"  minwaterdepth mindistancefromprefabs	smoothing	- Creates a new map file with cargopath of these settings embeded
// /showcargopath																	- Shows the current loaded cargopath (squares nodes wont let cargo leave or spawn on)
// /addcargopath nodeindex															- Adds at node at that index
// /removecargopath nodeindex nodeindex												- Removes node at that index or all between 2 given indexes
// /savecargopath "newmapname.map"													- Saves current cargopath into new mapfile
// /blockcargopath																	- Changes Topology below player to what you have set to block cargo egrees

namespace Oxide.Plugins
{
	[Info("CargoPathController", "bmgjet", "0.0.1")]
	[Description("CargoPathController Tool")]

	class CargoPathController : RustPlugin
	{
		public int TopologyBaseBlock = TerrainTopology.OCEANSIDE;
		public string NewMapName;
		public float MinWaterhDepth;
		public float MinDistanceFromShore;
		public int Smoothing;
		private List<Vector3> NewPath = new List<Vector3>();
		private Vector3 LastNode = Vector3.zero;
		private Timer ViewPath;

		private void Unload() { ServerMgr.Instance.StopAllCoroutines(); if (ViewPath != null) { ViewPath.Destroy(); } }

		private object OnCargoShipEgress(CargoShip cs)
		{
			if (cs != null)
			{
				if (TerrainMeta.TopologyMap.GetTopology(cs.transform.position, TopologyBaseBlock))
				{
					Timer CheckEgress = timer.Once(30f, () => { cs.StartEgress(); });
					return true;
				}
			}
			return null;
		}

		private void OnEntitySpawned(CargoShip cs) { if (cs != null) { cs.transform.position = DefaultRandomPos(); } }

		[ChatCommand("blockcargopath")]
		private void blockcargopath(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin)
			{
				TerrainMeta.TopologyMap.SetTopology(player.transform.position, TopologyBaseBlock,20,2);
				player.ChatMessage("Changed Topology here to " + TopologyBaseBlock.ToString());
			}
		}

		[ChatCommand("addcargopath")]
		private void addcargopath(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin)
			{
				if (Args != null)
				{
					if (Args.Length == 1)
					{
						Vector3 pos = player.transform.position;
						pos.y = 0;
						if (Args[0].ToLower().Contains("here"))
						{
							int index = GetClosestNodeToUs(pos);
							TerrainMeta.Path.OceanPatrolFar.Insert(index, pos);
							player.ChatMessage("Added Cargopath Node @ " + index.ToString());
							return;
						}
						try
						{
							int node = int.Parse(Args[0]);
							TerrainMeta.Path.OceanPatrolFar.Insert(node, pos);
							player.ChatMessage("Added Cargopath Node @ " + node.ToString());
							return;
						}
						catch { }
					}
				}
				player.ChatMessage("Incorrect args");
				player.ChatMessage("/addcargopath nodeindex");
			}
		}

		[ChatCommand("removecargopath")]
		private void removecargopath(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin)
			{
				if (Args != null)
				{
					if (Args.Length == 1)
					{
						try
						{
							int node = int.Parse(Args[0]);
							TerrainMeta.Path.OceanPatrolFar.RemoveAt(node);
							player.ChatMessage("Removed Cargopath Node");
							return;
						}
						catch { }
					}
					if (Args.Length == 2)
					{
						try
						{
							int node = int.Parse(Args[0]);
							int node2 = int.Parse(Args[1]);
							TerrainMeta.Path.OceanPatrolFar.RemoveRange(node, node2 - node);
							player.ChatMessage("Removed Cargopath Node");
							return;
						}
						catch { }
					}
				}
				player.ChatMessage("Incorrect args");
				player.ChatMessage("/removecargopath nodeindex");
			}
		}

		[ChatCommand("showcargopath")]
		private void shownewcargopath(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin)
			{
				if (ViewPath != null)
				{
					ViewPath.Destroy();
					ViewPath = null;
					player.ChatMessage("Stopped Viewing Cargo Path");
					return;
				}
				int distance = 2000;
				if (Args != null && Args.Length == 1)
				{
					distance = int.Parse(Args[0]);
				}
				player.ChatMessage("Started Viewing Cargo Path");
				ViewPath = timer.Every(2f, () =>
				{
					int nodeindex = 0;
					foreach (Vector3 vector in TerrainMeta.Path.OceanPatrolFar)
					{
						if (Vector3.Distance(vector, player.transform.position) < distance)
						{
							Vector3 pos = vector;
							pos.y = 0;
							if (TerrainMeta.TopologyMap.GetTopology(vector, TopologyBaseBlock)) { player.SendConsoleCommand("ddraw.box", 2f, Color.red, pos, 25f); }
							else { player.SendConsoleCommand("ddraw.sphere", 2f, Color.blue, pos, 25f); }
							if (LastNode != Vector3.zero) { player.SendConsoleCommand("ddraw.line", 2f, Color.blue, pos, LastNode); }
							player.SendConsoleCommand("ddraw.text", 2f, Color.white, pos, "<size=30>" + nodeindex.ToString() + "</size>");
							LastNode = pos;
						}
						else
						{
							LastNode = Vector3.zero;
						}
						nodeindex++;
					}
				});
			}
		}

		[ConsoleCommand("stopcargopath")]
		private void stopcargopath(ConsoleSystem.Arg arg)
		{
			if (!arg.IsAdmin) { return; }
			ServerMgr.Instance.StopAllCoroutines();
		}

		[ConsoleCommand("createcargopath")]
		private void createcargopath(ConsoleSystem.Arg arg)
		{
			if (!arg.IsAdmin) { return; }
			if (arg.Args != null)
			{
				ClearMapDataPath();
				if (arg.Args.Length == 1)
				{
					NewMapName = arg.Args[0];
					SaveMap();
					return;
				}
				if (arg.Args.Length == 4)
				{
					NewMapName = arg.Args[0];
					MinWaterhDepth = float.Parse(arg.Args[1]);
					MinDistanceFromShore = float.Parse(arg.Args[2]);
					Smoothing = int.Parse(arg.Args[3]);
					ServerMgr.Instance.StopAllCoroutines();
					ServerMgr.Instance.StartCoroutine(GenerateOceanPatrolPath());
					return;
				}
			}
			Puts("Incorrect args");
			Puts("createcargopath mapnewname (Saves current native path)");
			Puts("createcargopath mapnewname MinDepth minDistanceFromPrefabs smoothing (Creates New Native path with settings and saves it)");
		}

		[ChatCommand("createcargopath")]
		private void createcargopathcmd(BasePlayer player, string command, string[] args)
		{
			if (!player.IsAdmin) { return; }
			if (args != null)
			{
				ClearMapDataPath();
				if (args.Length == 4)
				{
					NewMapName = args[0];
					MinWaterhDepth = float.Parse(args[1]);
					MinDistanceFromShore = float.Parse(args[2]);
					Smoothing = int.Parse(args[3]);
					ServerMgr.Instance.StopAllCoroutines();
					ServerMgr.Instance.StartCoroutine(GenerateOceanPatrolPath());
					return;
				}
			}
			player.ChatMessage("Incorrect args");
			player.ChatMessage("/createcargopath mapnewname MinDepth minDistanceFromPrefabs smoothing (Creates New Native path with settings and saves it)");
		}

		[ChatCommand("savecargopath")]
		private void savecargopath(BasePlayer player, string command, string[] args)
		{
			if (!player.IsAdmin) { return; }
			if (args != null)
			{
				if (args.Length == 1)
				{
					NewMapName = args[0];
					if (NewMapName.Contains(".map"))
					{
						ClearMapDataPath();
						SaveMap();
						return;
					}
				}
			}
			player.ChatMessage("Incorrect args");
			player.ChatMessage("/savecargopath mapnewname (Saves current cargo path)");
		}

		public int GetClosestNodeToUs(Vector3 position)
		{
			int result = 0;
			float num = float.PositiveInfinity;
			for (int i = 0; i < global::TerrainMeta.Path.OceanPatrolFar.Count; i++)
			{
				Vector3 b = global::TerrainMeta.Path.OceanPatrolFar[i];
				float num2 = Vector3.Distance(position, b);
				if (num2 < num)
				{
					result = i;
					num = num2;
				}
			}
			return result;
		}

		private Vector3 DefaultRandomPos()
		{
			Vector3 newpos;
			for (int i = 0; i < 1000; i++)
			{
				newpos = TerrainMeta.Path.OceanPatrolFar.GetRandom();
				newpos.y = TerrainMeta.WaterMap.GetHeight(newpos);
				if (!TerrainMeta.TopologyMap.GetTopology(newpos, TopologyBaseBlock)) { return newpos; }
			}
			newpos = TerrainMeta.RandomPointOffshore();
			newpos.y = TerrainMeta.WaterMap.GetHeight(newpos);
			return newpos;
		}

		private void ClearMapDataPath()
		{
			string RustEditCargoPath = MapDataName(World.Serialization.world.prefabs.Count);
			for (int i = World.Serialization.world.maps.Count - 1; i >= 0; i--)
			{
				if (World.Serialization.world.maps[i].name == RustEditCargoPath) { World.Serialization.world.maps.Remove(World.Serialization.world.maps[i]); }
			}
		}

		private void UpdateMapTopologyDataPath()
		{
			string RustEditCargoPath = MapDataName(World.Serialization.world.prefabs.Count);
			for (int i = World.Serialization.world.maps.Count - 1; i >= 0; i--)
			{
				if (World.Serialization.world.maps[i].name == "topology") 
				{
					World.Serialization.world.maps[i].data = TerrainMeta.TopologyMap.ToByteArray();
				}
			}
		}

		public void SaveMap()
		{
			UpdateMapTopologyDataPath();
			MapData mapData = new MapData();
			mapData.name = MapDataName(World.Serialization.world.prefabs.Count);
			mapData.data = Serialise();
			World.Serialization.world.maps.Add(mapData);
			World.Serialization.Save(NewMapName);
			adminmessage("Saved " + NewMapName);
		}

		public void adminmessage(string msg) { Puts(msg); foreach (BasePlayer bp in BasePlayer.activePlayerList) { if (bp.IsAdmin) { bp.ChatMessage(msg); } } }

		public string MapDataName(int PreFabCount)
		{
			using (Aes aes = Aes.Create())
			{
				Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(PreFabCount.ToString(), new byte[] { 73, 118, 97, 110, 32, 77, 101, 100, 118, 101, 100, 101, 118 });
				aes.Key = rfc2898DeriveBytes.GetBytes(32);
				aes.IV = rfc2898DeriveBytes.GetBytes(16);
				using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream())
				{
					using (CryptoStream cryptoStream = new CryptoStream(memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
					{
						cryptoStream.Write(new byte[] { 0x6f, 0x00, 0x63, 0x00, 0x65, 0x00, 0x61, 0x00, 0x6e, 0x00, 0x70, 0x00, 0x61, 0x00, 0x74, 0x00, 0x68, 0x00, 0x70, 0x00, 0x6f, 0x00, 0x69, 0x00, 0x6e, 0x00, 0x74, 0x00, 0x73, 0x00 }, 0, 30);
						cryptoStream.Close();
					}
					return Convert.ToBase64String(memoryStream.ToArray());
				}
			}
		}

		public IEnumerator GenerateOceanPatrolPath()
		{
			adminmessage("Creating a new path grid may take awhile");
			float NextMessageTick = Time.time;
			var checks = 0;
			var _instruction = ConVar.FPS.limit > 20 ? CoroutineEx.waitForSeconds(0.01f) : null;
			float x = TerrainMeta.Size.x;
			float num = x * 2f * 3.14159274f;
			float num2 = 30f;
			int num3 = Mathf.CeilToInt(num / num2);
			List<Vector3> list = new List<Vector3>();
			float num4 = x;
			float y = 0f;
			for (int i = 0; i < num3; i++)
			{
				float num5 = (float)i / (float)num3 * 360f;
				list.Add(new Vector3(Mathf.Sin(num5 * 0.0174532924f) * num4, y, Mathf.Cos(num5 * 0.0174532924f) * num4));
			}
			float d = 4f;
			float num6 = 200f;
			bool flag = true;
			int num7 = 0;
			while (num7 < ConVar.AI.ocean_patrol_path_iterations && flag)
			{
				flag = false;
				for (int j = 0; j < num3; j++)
				{
					Vector3 vector = list[j];
					int index = (j == 0) ? (num3 - 1) : (j - 1);
					int index2 = (j == num3 - 1) ? 0 : (j + 1);
					Vector3 b = list[index2];
					Vector3 b2 = list[index];
					Vector3 origin = vector;
					Vector3 normalized = (Vector3.zero - vector).normalized;
					Vector3 vector2 = vector + normalized * d;
					if (Vector3.Distance(vector2, b) <= num6 && Vector3.Distance(vector2, b2) <= num6)
					{
						bool flag2 = true;
						int num8 = 16;
						for (int k = 0; k < num8; k++)
						{
							if (++checks >= 10000)
							{
								if (NextMessageTick < Time.time)
								{
									adminmessage("Cargo Path Gen: Please Wait");
									NextMessageTick = Time.time + 5;
								}
								checks = 0;
								yield return _instruction;
							}
							float num9 = (float)k / (float)num8 * 360f;
							Vector3 normalized2 = new Vector3(Mathf.Sin(num9 * 0.0174532924f), y, Mathf.Cos(num9 * 0.0174532924f)).normalized;
							Vector3 vector3 = vector2 + normalized2 * 1f;
							Vector3 direction = normalized;
							if (vector3 != Vector3.zero)
							{
								direction = (vector3 - vector2).normalized;
							}
							RaycastHit raycastHit;
							if ((TerrainMeta.HeightMap.GetHeight(vector2) * -1) <= MinWaterhDepth) { flag2 = false; break; }
							if (UnityEngine.Physics.SphereCast(origin, 3f, direction, out raycastHit, MinDistanceFromShore, 1218511105))
							{
								flag2 = false;
								break;
							}
						}
						if (flag2)
						{
							flag = true;
							list[j] = vector2;
							foreach (BasePlayer bp in BasePlayer.activePlayerList)
							{
								if (bp.IsAdmin)
								{
									if (Vector3.Distance(bp.transform.position, vector2) < 1000)
									{
										bp.SendConsoleCommand("ddraw.sphere", 0.1f, Color.blue, vector2, 1f);
									}
								}
							}
						}
					}
				}
				num7++;
			}
			List<int> list2 = new List<int>();
			LineUtility.Simplify(list, Smoothing, list2);
			List<Vector3> list3 = list;
			list = new List<Vector3>();
			foreach (int index3 in list2) { list.Add(list3[index3]); }
			for (int i = 0; i < list.Count - 1; i++)
			{
				try
				{
					float dis = Vector3.Distance(list[i], list[i + 1]);
					for (int i2 = i + 2; i2 < list.Count - 1; i2++)
					{
						if (Vector3.Distance(list[i], list[i2]) <= dis)
						{
							list.RemoveRange(i + 1, i2 - i);
							break;
						}
					}
				}
				catch { }
			}
			adminmessage("saving created grid " + list.Count.ToString());
			TerrainMeta.Path.OceanPatrolFar = list;
			SaveMap();
			yield break;
		}

		public byte[] Serialise()
		{
			List<byte> newdata = new List<byte>();
			newdata.AddRange(Convert.FromBase64String("PD94bWwgdmVyc2lvbj0iMS4wIj8+CjxTZXJpYWxpemVkUGF0aExpc3QgeG1sbnM6eHNkPSJodHRwOi8vd3d3LnczLm9yZy8yMDAxL1hNTFNjaGVtYSIgeG1sbnM6eHNpPSJodHRwOi8vd3d3LnczLm9yZy8yMDAxL1hNTFNjaGVtYS1pbnN0YW5jZSI+CiAgPHZlY3RvckRhdGE+CiAgICA="));
			
			foreach (Vector3 vect in TerrainMeta.Path.OceanPatrolFar)
			{
				newdata.AddRange(Convert.FromBase64String("ICAgIDxWZWN0b3JEYXRhPg=="));
				newdata.AddRange(Convert.FromBase64String("ICAgICAgPHg+"));
				newdata.AddRange(Encoding.ASCII.GetBytes(vect.x.ToString()));
				newdata.AddRange(Convert.FromBase64String("PC94PgogICAgICA8eT4wPC95PgogICAgICA8ej4="));
				newdata.AddRange(Encoding.ASCII.GetBytes(vect.z.ToString()));
				newdata.AddRange(Convert.FromBase64String("PC96PgogICAgPC9WZWN0b3JEYXRhPg=="));
			}
			newdata.AddRange(Convert.FromBase64String("CiAgPC92ZWN0b3JEYXRhPgo8L1NlcmlhbGl6ZWRQYXRoTGlzdD4="));
			return newdata.ToArray();
		}
	}
}