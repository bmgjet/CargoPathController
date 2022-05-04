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
// /blockcargopath blocksize														- Changes Topology below player to what you have set to block cargo egrees
// /crashcargoship																	- Enables crash mode and sends cargo towards vector zero
// /overridecargopath																- Toggles the cargo spawn point override
namespace Oxide.Plugins
{
	[Info("CargoPathController", "bmgjet", "0.0.2")]
	[Description("CargoPathController Tool")]

	class CargoPathController : RustPlugin
	{
		public int StoppedSeconds = 60;
		public bool AllowCargoCrash = false;
		public int TopologyBaseBlock = TerrainTopology.OCEANSIDE;
		public int TopologyBaseStop = TerrainTopology.MONUMENT;
		public static CargoPathController plugin;
		public string NewMapName;
		public float MinWaterhDepth;
		public float MinDistanceFromShore;
		public int Smoothing;
		private Vector3 LastNode = Vector3.zero;
		private Timer ViewPath;
		private List<CargoMod> cargoships = new List<CargoMod>();
		private bool OverRideSpawn = true;
		private Coroutine pathgenerator;

		class CargoMod : FacepunchBehaviour
		{
			public CargoShip _cargoship;
			public bool HasStopped = false;
			public Timer topochecker;
			public int LastNode = -1;
			public List<ScientistNPC> NPCs = new List<ScientistNPC>();
			public void StopCargoShip(float seconds)
            {
                if (_cargoship == null) { return; }
                LastNode = _cargoship.targetNodeIndex;
                if (LastNode == -1) { LastNode = _cargoship.GetClosestNodeToUs(); }
                _cargoship.targetNodeIndex = -1;
                _cargoship.currentThrottle = 0;
				_cargoship.Invoke(() => { if (_cargoship != null) { _cargoship.targetNodeIndex = LastNode; LastNode = -1; plugin.timer.Once(60f, () => { try { HasStopped = false; } catch { } }); } }, seconds);
			}

			public void CrashMe()
            {
				if (_cargoship == null) { return; }
				_cargoship.targetNodeIndex = -1;
				plugin.AllowCargoCrash = true;
				UpdateMovement();
			}

			public void NPCDrown()
            {
				if(NPCs.Count < 1) { return; }
				foreach(ScientistNPC npc in NPCs)
                {
					if (npc == null || npc.IsDestroyed) { continue;}
					if(npc.IsAlive() && npc.transform.position.y < -2){npc.Hurt(30f);}
                }
				plugin.timer.Once(1.5f, () => { NPCDrown(); });
            }

			public void Sink()
            {
				if (_cargoship != null && HasStopped)
				{
					if (NPCs.Count == 0) { foreach (BaseEntity b in _cargoship.children.ToArray()) { if (b is ScientistNPC) { NPCs.Add(b as ScientistNPC); } } NPCDrown(); }
					_cargoship.transform.position += _cargoship.transform.up * -1f * Time.deltaTime;
					_cargoship.Invoke(() => { Sink(); }, 0.02f);
					if (_cargoship.transform.position.y <= -45)
					{
						plugin.timer.Once(1f, () =>
						{
							foreach (BaseEntity b in _cargoship.children.ToArray())
							{
								if (b != null)
								{
									if (b is BasePlayer || b is LootableCorpse)
									{
										Vector3 oldpos = b.transform.position;
										oldpos.y = 1;
										b.SetParent(null, true, true);
									}
								}
							}
							plugin.NextFrame(() => { if (!_cargoship.IsDestroyed) { _cargoship.Kill(); } });
						});
					}
				}
			}

			public void UpdateMovement()
			{
				if (_cargoship != null && !HasStopped)
				{
					Vector3 normalized = (new Vector3(0, 0, 0) - _cargoship.transform.position).normalized;
					float Direction = Vector3.Dot(_cargoship.transform.right, (new Vector3(0, 0, 0) - _cargoship.transform.position).normalized);
					_cargoship.turnScale = Mathf.Lerp(_cargoship.turnScale, Mathf.InverseLerp(0.05f, 0.5f, Mathf.Abs(Direction)), Time.deltaTime * 0.2f);
					_cargoship.currentTurnSpeed = 4f * _cargoship.turnScale * (float)((Direction < 0f) ? -1 : 1);
					_cargoship.transform.Rotate(Vector3.up, Time.deltaTime * _cargoship.currentTurnSpeed, Space.World);
					_cargoship.currentThrottle = Mathf.Lerp(_cargoship.currentThrottle, Mathf.InverseLerp(0f, 1f, Vector3.Dot(_cargoship.transform.forward, normalized)), Time.deltaTime * 0.2f);
					_cargoship.currentVelocity = _cargoship.transform.forward * (12f * _cargoship.currentThrottle);
					_cargoship.transform.position += _cargoship.currentVelocity * Time.deltaTime;
					_cargoship.Invoke(()=> { UpdateMovement(); },0.005f);
				}
			}

			public void playexp(List<Vector3> ExPoint)
			{
				if (ExPoint != null && ExPoint.Count > 1)
				{
					plugin.RunEffect("assets/prefabs/tools/c4/effects/c4_explosion.prefab", null, ExPoint[0]);
					plugin.RunEffect("assets/prefabs/npc/patrol helicopter/damage_effect_debris.prefab", null, ExPoint[1]);
					ExPoint.RemoveAt(0);
					ExPoint.RemoveAt(0);
					plugin.timer.Once(0.5f, () => { playexp(ExPoint); });
					return;
				}
				Sink();
			}

			public void CrashCargo(int seconds, List<Vector3> bow)
			{
				if (_cargoship == null) { return; }
				_cargoship.SetFlag(global::BaseEntity.Flags.Reserved8, true, false, true);
				_cargoship.targetNodeIndex = -1;
				_cargoship.currentThrottle = 0;
				plugin.timer.Once(seconds, () => { if (bow != null) { playexp(bow); } });
			}

			public Vector3 AllowedRandomPos()
			{
				if (_cargoship == null) { return TerrainMeta.RandomPointOffshore(); }
				Vector3 newpos;
				int randomnode = UnityEngine.Random.Range(0, TerrainMeta.Path.OceanPatrolFar.Count);
				for (int i = randomnode; i < TerrainMeta.Path.OceanPatrolFar.Count - 1; i++)
				{
					newpos = TerrainMeta.Path.OceanPatrolFar[i];
					newpos.y = TerrainMeta.WaterMap.GetHeight(newpos);
					if (!TerrainMeta.TopologyMap.GetTopology(newpos, plugin.TopologyBaseBlock)) { return newpos; }
				}
				for (int i = 0; i < TerrainMeta.Path.OceanPatrolFar.Count - 1; i++)
				{
					newpos = TerrainMeta.Path.OceanPatrolFar[i];
					newpos.y = TerrainMeta.WaterMap.GetHeight(newpos);
					if (!TerrainMeta.TopologyMap.GetTopology(newpos, plugin.TopologyBaseBlock)) { return newpos; }
				}
				newpos = TerrainMeta.RandomPointOffshore();
				newpos.y = TerrainMeta.WaterMap.GetHeight(newpos);
				return newpos;
			}

			public void CargoTick()
			{
				if (_cargoship == null) { return; }
				Vector3 bow = _cargoship.transform.position + (_cargoship.transform.forward * 81) + (_cargoship.transform.up * 1);
				Vector3 port = _cargoship.transform.position + (_cargoship.transform.forward * 56) + (_cargoship.transform.right * 12);
				Vector3 starboard = _cargoship.transform.position + (_cargoship.transform.forward * 56) + (_cargoship.transform.right * -12);
				if (plugin.AllowCargoCrash)
				{
					if (!HasStopped && (TerrainMeta.HeightMap.GetHeight(bow) >= -0.5f || TerrainMeta.HeightMap.GetHeight(port) >= -0.5f || TerrainMeta.HeightMap.GetHeight(starboard) >= -0.5f))
					{
						List<Vector3> ExpoPos = new List<Vector3>();
						for (int i = 0; i < 9; i++)
						{
							ExpoPos.Add(bow + (_cargoship.transform.forward * -32) + (_cargoship.transform.forward * (-10 * i)) + (_cargoship.transform.right * 6));
							ExpoPos.Add(bow + (_cargoship.transform.forward * -32) + (_cargoship.transform.forward * (-10 * i)) + (_cargoship.transform.right * -6));
						}
						plugin.RunEffect("assets/prefabs/tools/c4/effects/c4_explosion.prefab", null, bow +(_cargoship.transform.forward * -7) + (_cargoship.transform.right * 4));
						plugin.RunEffect("assets/prefabs/npc/patrol helicopter/damage_effect_debris.prefab", null, bow + (_cargoship.transform.forward * -7) + (_cargoship.transform.right * 4));
						plugin.RunEffect("assets/prefabs/tools/c4/effects/c4_explosion.prefab", null, bow + (_cargoship.transform.forward * -7) + (_cargoship.transform.right * -4));
						plugin.RunEffect("assets/prefabs/npc/patrol helicopter/damage_effect_debris.prefab", null, bow + (_cargoship.transform.forward * -7) + (_cargoship.transform.right * -4));
						HasStopped = true;
						CrashCargo(10, ExpoPos);
					}
				}
				if (!HasStopped && TerrainMeta.TopologyMap.GetTopology(_cargoship.transform.position, plugin.TopologyBaseStop))
				{
					HasStopped = true;
					StopCargoShip(plugin.StoppedSeconds);
				}
			}
		}

		private void Init() { plugin = this; }
		private void OnServerInitialized(bool initial) { if (initial) { DelayStaryUp(); } else { Startup(); } }
		private void DelayStaryUp() { timer.Once(10f, () => { try { if (Rust.Application.isLoading) { DelayStaryUp(); return; } } catch { } Startup(); }); }
		private void Startup() { foreach (BaseNetworkable b in BaseNetworkable.serverEntities.entityList.Values) { if (b is CargoShip) { ApplyCargoMod(b as CargoShip, true); } } }
		private void Unload() { if (pathgenerator != null) { ServerMgr.Instance.StopCoroutine(pathgenerator); } if (ViewPath != null) { ViewPath.Destroy(); } foreach (CargoMod cm in cargoships) { if (cm.topochecker != null) { cm.topochecker.Destroy(); } if (cm._cargoship != null) { GameObject.Destroy(cm); } } plugin = null; }
		private object OnCargoShipEgress(CargoShip cs)
		{
			if (cs != null)
			{
                if (TerrainMeta.TopologyMap.GetTopology(cs.transform.position, TopologyBaseBlock))
                {
                    timer.Once(30f, () => { if (cs != null) { cs.StartEgress(); } });
                    return true;
                }
				foreach (CargoMod cm in cargoships) { if (cm._cargoship == cs) { cargoships.Remove(cm); if (cm.topochecker != null) { cm.topochecker.Destroy(); } GameObject.Destroy(cm); } }
				List<BaseEntity> kill = new List<BaseEntity>();
				foreach (BaseEntity b in cs.children.ToArray()) { kill.Add(b); }
				timer.Once(60, () =>
				{
					if (kill != null)
						foreach (BaseEntity b in kill.ToArray())
						{
							if (b != null && !b.IsDestroyed)
							{
								b.Kill();
							}
						}
				});
			}
			return null;
		}
		private void OnEntitySpawned(CargoShip cs) { ApplyCargoMod(cs); }

		private object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
		{
			if (player != null)
			{
				BaseEntity be = player.GetParentEntity();
				if(be != null && be is CargoShip)
                {
					player.Hurt(30f);
                }
				return false;
			}
			return null;
		}

		[ChatCommand("crashcargoship")]
		private void crashcargoship(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin){foreach(CargoMod cm in cargoships){if (cm._cargoship != null){cm.CrashMe();}}}
		}

		[ChatCommand("blockcargopath")]
		private void blockcargopath(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin)
			{
				int blocksize = 50;
				if (Args != null && Args.Length == 1) { blocksize = int.Parse(Args[0]); }
				TerrainMeta.TopologyMap.SetTopology(player.transform.position, TopologyBaseBlock, blocksize, 2);
				player.ChatMessage("Changed Topology here to " + TopologyBaseBlock.ToString());
			}
		}

		[ChatCommand("overridecargopath")]
		private void overridecargopath(BasePlayer player, string command, string[] Args)
		{
			if (player.IsAdmin)
			{
				OverRideSpawn = !OverRideSpawn;
				player.ChatMessage("Override Cargo Spawn Location:" + OverRideSpawn);
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
							if (TerrainMeta.TopologyMap.GetTopology(vector, TopologyBaseStop)) { player.SendConsoleCommand("ddraw.text", 2f, Color.red, pos, "<size=30>Stop Trigger</size>"); }
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
			if (pathgenerator != null) { ServerMgr.Instance.StopCoroutine(pathgenerator); }
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
					if (pathgenerator != null) { ServerMgr.Instance.StopCoroutine(pathgenerator); }
					pathgenerator = ServerMgr.Instance.StartCoroutine(GenerateOceanPatrolPath());
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
					if (pathgenerator != null) { ServerMgr.Instance.StopCoroutine(pathgenerator); }
					pathgenerator = ServerMgr.Instance.StartCoroutine(GenerateOceanPatrolPath());
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

		private void ApplyCargoMod(CargoShip cs, bool reload = false)
		{
			if (cs != null)
			{
				if (cs.gameObject.HasComponent<CargoMod>()) { return; }
				CargoMod newcargoship = cs.gameObject.AddComponent<CargoMod>();
				newcargoship._cargoship = cs;
				cargoships.Add(newcargoship);
				if (OverRideSpawn && !reload) { cs.transform.position = newcargoship.AllowedRandomPos(); }
				newcargoship.topochecker = timer.Every(1f, () => { newcargoship.CargoTick(); });
			}
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
		private void RunEffect(string name, BaseEntity entity = null, Vector3 position = new Vector3(), Vector3 offset = new Vector3())
		{
			if (entity != null) { Effect.server.Run(name, entity, 0, offset, position, null, true); return; }
			Effect.server.Run(name, position, Vector3.up, null, true);
		}

		public IEnumerator GenerateOceanPatrolPath()
		{
			adminmessage("Creating a new path grid may take awhile");
			float NextMessageTick = Time.time;
			var checks = 0;
			int limiterrate = 10000;
			if (MinDistanceFromShore == 0) { limiterrate *= 5; }
			if (MinWaterhDepth == 0) { limiterrate *= 2; }
			var _instruction = ConVar.FPS.limit > 20 ? CoroutineEx.waitForSeconds(0.01f) : null;
			int points = Mathf.CeilToInt(TerrainMeta.Size.x * 2f * 3.14159274f / 16); //30
			List<Vector3> list = new List<Vector3>();
			for (int i = 0; i < points; i++)
			{
				float num5 = (float)i / (float)points * 360f;
				list.Add(new Vector3(Mathf.Sin(num5 * 0.0174532924f) * TerrainMeta.Size.x, 0, Mathf.Cos(num5 * 0.0174532924f) * TerrainMeta.Size.x));
			}
			bool flag = true;
			int num7 = 0;
			while (num7 < ConVar.AI.ocean_patrol_path_iterations && flag)
			{
				flag = false;
				for (int j = 0; j < points; j++)
				{
					Vector3 vector = list[j];
					int index = (j == 0) ? (points - 1) : (j - 1);
					int index2 = (j == points - 1) ? 0 : (j + 1);
					Vector3 origin = vector;
					Vector3 normalized = (Vector3.zero - vector).normalized;
					Vector3 vector2 = vector + normalized * 4;
					if (Vector3.Distance(vector2, list[index2]) <= 200 && Vector3.Distance(vector2, list[index]) <= 200)
					{
						bool flag2 = true;
						int num8 = 16;
						for (int k = 0; k < num8; k++)
						{
							if (++checks >= limiterrate)
							{
								if (NextMessageTick < Time.time)
								{
									adminmessage("Cargo Path Gen: Please Wait");
									NextMessageTick = Time.time + 5;
								}
								checks = 0;
								yield return _instruction;
							}
							if (TerrainMeta.TopologyMap.GetTopology(vector2, TerrainTopology.MAINLAND)) { flag2 = false; break; }
							if (MinWaterhDepth != 0)
							{
								if ((TerrainMeta.HeightMap.GetHeight(vector2) * -1) <= MinWaterhDepth) { flag2 = false; break; }
							}
							if (MinDistanceFromShore != 0)
							{
								float num9 = (float)k / (float)num8 * 360f;
								Vector3 normalized2 = new Vector3(Mathf.Sin(num9 * 0.0174532924f), 0, Mathf.Cos(num9 * 0.0174532924f)).normalized;
								Vector3 vector3 = vector2 + normalized2 * 1f;
								Vector3 direction = normalized;
								if (vector3 != Vector3.zero) { direction = (vector3 - vector2).normalized; }
								RaycastHit raycastHit;
								if (UnityEngine.Physics.SphereCast(origin, 3f, direction, out raycastHit, MinDistanceFromShore, 1218511105))
								{
									flag2 = false;
									break;
								}
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
										bp.SendConsoleCommand("ddraw.sphere", 0.01f, Color.blue, vector2, 1f);
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