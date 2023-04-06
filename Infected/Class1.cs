using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BoneLib;
using BoneLib.BoneMenu.Elements;
using HarmonyLib;
using LabFusion;
using LabFusion.Data;
using LabFusion.Extensions;
using LabFusion.Network;
using LabFusion.Representation;
using LabFusion.SDK.Gamemodes;
using LabFusion.Senders;
using LabFusion.Utilities;
using MelonLoader;
using SLZ.Rig;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
using Avatar = SLZ.VRMK.Avatar;
using Random = UnityEngine.Random;

namespace Infected
{
	public class Class1 : MelonMod
	{
		public override void OnInitializeMelon()
		{
			GamemodeRegistration.LoadGamemodes(Assembly.GetExecutingAssembly());
			var assetBundle = EmbeddedAssetBundle.LoadFromAssembly(System.Reflection.Assembly.GetExecutingAssembly(), "Infected.Resources.megabundle.bigpack");
			InfectedGamemode.BeginningAudioClip = assetBundle.LoadPersistentAsset<AudioClip>("assets/sfxforinf/beginningtrack.mp3");
			if (InfectedGamemode.BeginningAudioClip != null)
			{
				MelonLogger.Msg("Beginning audio is NOT null bgurh");
			}
			InfectedGamemode.ActionMusic = assetBundle.LoadPersistentAsset<AudioClip>("assets/sfxforinf/actiontrack.wav");
			if (InfectedGamemode.ActionMusic != null)
			{
				MelonLogger.Msg("Action audio is NOT null bgurh");
			}
			InfectedGamemode.InfectedImage = assetBundle.LoadPersistentAsset<Texture2D>("assets/sfxforinf/inficon.png");
			if (InfectedGamemode.InfectedImage != null)
			{
				MelonLogger.Msg("Image is NOT null bgurh");
			}
		}

		public override void OnLateInitializeMelon()
		{
			HarmonyInstance.Patch(typeof(Avatar).GetMethod(nameof(Avatar.ComputeBaseStats), AccessTools.all), null, new HarmonyMethod(typeof(Patch).GetMethod(nameof(Patch.PostFix))));
			
		}
	}
	
	public static class Patch
	{
		public static void PostFix(Avatar __instance)
		{
			MelonLogger.Msg("changing speed");
			if (InfectedGamemode.shouldModifySpeed)
			{
				__instance._speed *= 1.5f;
				__instance._agility *= 1.5f;
				__instance._strengthLower *= 1.5f;
				MelonLogger.Msg("changed speed");
			}
		}
	}

	public static class AssetBundleExtensioner
	{
		public static T LoadPersistentAsset<T>(this AssetBundle bundle, string name) where T : UnityEngine.Object {
			var asset = bundle.LoadAsset(name);

			if (asset != null) {
				asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
				return asset.TryCast<T>();
			}

			return null;
		}
	}
	public class InfectedGamemode : Gamemode
	{
		public static AudioClip BeginningAudioClip;
		public static AudioClip ActionMusic;
		public static Texture2D InfectedImage;
		
		public static Gamemode Instance { get; private set; }
		
		public const string DefaultPrefix = "InternalInfectedMetadata";
		
		public const string PlayerInfKey = DefaultPrefix + ".InfectionState";
		
		public override string GamemodeCategory => "Infected";
		public override string GamemodeName => "1 Infected";
		
		public override bool AutoStopOnSceneLoad => true;
		public override bool DisableSpawnGun => true;
		public override bool DisableDevTools => true;
		public override bool DisableManualUnragdoll => true;

		public static bool shouldModifySpeed;

		public float defaultPlayerSpeed;

		public List<PlayerId> allPlayers = new List<PlayerId>();

		public List<PlayerId> infectedPlayers = new List<PlayerId>();

		public Dictionary<PlayerId, TeamLogoInstance> TeamLogoInstances = new Dictionary<PlayerId, TeamLogoInstance>();

		public PlayerId infectedPlayer;

		public int minutes = 5;

		public bool isPlayerMortal = false;
		public bool isPlayerInfected = false;

		public bool gameOverSatisfied = false;
		public bool infWin = false;

		public bool isInfRevealed = false;
		
		public Stopwatch stopwatch;

		public override void OnBoneMenuCreated(MenuCategory category) {
			base.OnBoneMenuCreated(category);

			category.CreateIntElement("Minutes To Survive", Color.white, 5, 1, 1, 20, (v) => {
				minutes = v;
			});
		}
		
		public override void OnGamemodeRegistered() 
		{
			Instance = this;

			// Add hooks
			MultiplayerHooking.OnPlayerAction += OnPlayerAction;
			MultiplayerHooking.OnPlayerLeave += OnPlayerLeave;
		}
		
		public override void OnGamemodeUnregistered() {
			if (Instance == this)
				Instance = null;

			// Remove hooks
			MultiplayerHooking.OnPlayerAction -= OnPlayerAction;
			MultiplayerHooking.OnPlayerLeave -= OnPlayerLeave;
		}
		
		protected void OnPlayerLeave(PlayerId id) {
			if (TeamLogoInstances.TryGetValue(id, out var instance)) 
			{
				instance.Cleanup();
				TeamLogoInstances.Remove(id);
			}
		}

		protected void OnPlayerAction(PlayerId player, PlayerActionType type, PlayerId otherPlayer = null) 
		{
			// Kill detection
			if (IsActive() && NetworkInfo.IsServer) {
				switch (type) 
				{
					case PlayerActionType.DEATH_BY_OTHER_PLAYER:
						// Checks if its a non-infected killed by and infected
						// and sets the metadata to tell the other clients (including the killed one) to make that person infected
						if (infectedPlayers.Contains(otherPlayer) && otherPlayer != player && !infectedPlayers.Contains(player)) 
						{
							TrySetMetadata(GetInfKey(player), "INFECTED");
						}
						break;
				}
			}
		}
		
		public class TeamLogoInstance 
		{
            protected const float LogoDivider = 270f;

            public TeamDeathmatch deathmatch;

            public GameObject go;
            public Canvas canvas;
            public RawImage image;

            public PlayerId id;
            public PlayerRep rep;
            
            public TeamLogoInstance(PlayerId id) 
            {
	            go = new GameObject($"{id.SmallId} Team Logo");

                canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 100000;
                go.transform.localScale = Vector3.one / LogoDivider;

                image = go.AddComponent<RawImage>();

                GameObject.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.DontUnloadUnusedAsset;

                this.id = id;
                PlayerRepManager.TryGetPlayerRep(id, out rep);
                
                UpdateLogo();
            }
            
            public void Cleanup() 
            {
	            if (go)
	            {
		            GameObject.Destroy(go);
	            }
            }

            public void Toggle(bool value) {
                go.SetActive(value);
            }

            public void UpdateLogo()
            {
	            image.texture = InfectedImage;
            }
            
            public bool IsShown() => go.activeSelf;

            public void Update() 
            {
                if (rep != null) {
                    var rm = rep.RigReferences.RigManager;

                    if (rm) {
                        var head = rm.physicsRig.m_head;

                        go.transform.position = head.position + Vector3.up * rep.GetNametagOffset();
                        go.transform.LookAtPlayer();

                        UpdateLogo();
                    }
                }
            }
        }

		
		protected override void OnMetadataChanged(string key, string value) 
		{
			// Gets local PlayerId
			var playerKey = GetInfKey(PlayerIdManager.LocalId);
			if (value == "INFECTED")
			{
				// Gets the PlayerID sent by the TrySetMetadata's inf key 
				// then checks if the infected playerarray already has the new player
				// if it doesnt then it will add it to the array
				string[] split = key.Split('.');
				PlayerId THEPLAYER = PlayerIdManager.GetPlayerId(ulong.Parse(split[2]));
				MelonLogger.Msg("Adding infected player with ID " + split[2]);
				if (!infectedPlayers.Contains(THEPLAYER))
				{
					infectedPlayers.Add(PlayerIdManager.GetPlayerId(ulong.Parse(split[2])));
					
					MelonLogger.Msg("Infected player count = " + infectedPlayers.Count);
					if (infectedPlayers.Count == allPlayers.Count && IsActive())
					{
						gameOverSatisfied = true;
						infWin = true;
						if (NetworkInfo.IsServer)
						{
							StopGamemode();
						}
					}
				}
				
				// Checks to see if the recieved key is your own key, and if it is you become infected
				// If its not your id you will also become normal, but theres a check to see if this has already happened and if it has it wont do anything
				if (infectedPlayers.Count >= 2)
				{
					if (infectedPlayers.Count == 2)
					{
						SetPlaylist(DefaultMusicVolume, ActionMusic);
						SendNotificationPublic(new FusionNotification()
						{
							title = "THE INFECTED ARE REVEALED",
							showTitleOnPopup = true,
							message = "AVOID THEM AT ALL COST.",
							isMenuItem = false,
							isPopup = true
						});
						OnReveal();
					}
					foreach (var player in infectedPlayers)
					{
						if (PlayerIdManager.LocalId != player)
						{
							TeamLogoInstances[player].Toggle(true);
						}
					}
				}

				if (playerKey == key)
				{
					BecomeInfected();
				}
				else if (!isPlayerInfected)
				{
					BecomeNormal();
				}
				// Called either way, no need to repeat 
				FusionPlayer.SetAmmo(0);
				FusionPlayer.SetMortality(true);
			}
		}

		protected override void OnStartGamemode()
		{
			base.OnStartGamemode();
			MelonLogger.Msg("Checking if server");
			
			SetPlaylist(DefaultMusicVolume, BeginningAudioClip);

			foreach (var player in PlayerIdManager.PlayerIds)
			{
				allPlayers.Add(player);
				MelonLogger.Msg("Added new player to all players. New player count is " + allPlayers.Count);
				if (player != PlayerIdManager.LocalId)
				{
					TeamLogoInstance teamLogoInstance = new TeamLogoInstance(player);
					teamLogoInstance.Toggle(false);
					TeamLogoInstances.Add(player, teamLogoInstance);
				}
			}

			
			if (NetworkInfo.IsServer)
			{
				// Rolls a random player to become infected when the game starts
				MelonLogger.Msg("Doing a random range to get a random player");
				System.Random random = new System.Random();
				var infPlayerRange = random.Next(allPlayers.Count);
				MelonLogger.Msg(infPlayerRange);
				infectedPlayer = allPlayers[infPlayerRange];
				MelonLogger.Msg("Done doing random check");
				stopwatch = Stopwatch.StartNew();
				
				TrySetMetadata(GetInfKey(infectedPlayer), "INFECTED");
				MelonLogger.Msg("Sent Metadata");
			}
		}

		// Logic for becoming infected. Removes your ammo, makes you mortal, and sets your health to 1.5x. Will also give you a knife.
		public void BecomeInfected()
		{
			FusionPlayer.SetPlayerVitality(1.5f);

			if (isInfRevealed)
			{
				if (!RigData.HasPlayer) return;
				shouldModifySpeed = true;
				Player.rigManager.SwapAvatarCrate(Player.rigManager.AvatarCrate.Barcode.ID, false, null);
			}
			 
			isPlayerInfected = true;
			SendNotificationPublic(new FusionNotification()
			{
				title = "YOU ARE INFECTED",
				showTitleOnPopup = true,
				message = "KILL SURVIVORS TO WIN!",
				isMenuItem = false,
				isPopup = true,
				popupLength = 5f
			});
		}

		// Logic for becoming normal. Gives you ammo, Gives you 1911, makes you mortal, and sets the health to normal (around ford health)
		public void BecomeNormal()
		{
			if (!isPlayerMortal)
			{
				FusionPlayer.SetPlayerVitality(1);
				isPlayerMortal = true;
				
				SendNotificationPublic(new FusionNotification()
				{
					title = "YOU ARE A SURVIVOR",
					showTitleOnPopup = true,
					message = "SURVIVE THE HOARD TO WIN!",
					isMenuItem = false,
					isPopup = true,
					popupLength = 5f
				});
			}
		}

		public void OnReveal()
		{
			if (isPlayerInfected)
			{
				if (!RigData.HasPlayer) return;
				shouldModifySpeed = true;
				Player.rigManager.SwapAvatarCrate(Player.rigManager.AvatarCrate.Barcode.ID, false, null);
			}
			else
			{
				FusionPlayer.SetAmmo(1000);
			}

			isInfRevealed = true;
		}

		protected override void OnStopGamemode()
		{
			// Reset mortality
			FusionPlayer.ResetMortality();

			// Remove ammo
			FusionPlayer.SetAmmo(10000);

			// Reset overrides
			FusionPlayer.ClearAvatarOverride();
			FusionPlayer.ClearPlayerVitality();

			isPlayerMortal = false;
			isPlayerInfected = false;
			infectedPlayers.Clear();
			allPlayers.Clear();
			shouldModifySpeed = false;
			isInfRevealed = false;
			Player.rigManager.SwapAvatarCrate(Player.rigManager.AvatarCrate.Barcode.ID, false, null);
			
			foreach (var team in TeamLogoInstances.Values)
			{
				team.Cleanup();
			}
			TeamLogoInstances.Clear();

			if (gameOverSatisfied)
			{
				if (infWin)
				{
					SendNotificationPublic(new FusionNotification()
					{
						title = "INFECTED WIN!",
						showTitleOnPopup = true,
						message = "They eradicated all survivors!",
						isMenuItem = false,
						isPopup = true
					});
				}
				else
				{
					SendNotificationPublic(new FusionNotification()
					{
						title = "SURVIVORS WIN!!",
						showTitleOnPopup = true,
						message = "They outlasted the HOARD!",
						isMenuItem = false,
						isPopup = true
					});
				}
				// Bit Reward
				RewardBits();
			}
			else
			{
				SendNotificationPublic(new FusionNotification()
				{
					title = "Infected Match Ended",
					showTitleOnPopup = true,
					message = "The match ended sooner than expected",
					isMenuItem = false,
					isPopup = true
				});
			}

			gameOverSatisfied = false;
			infWin = false;
		}

		private void RewardBits() {

			int reward, floor, roof;

			if(infectedPlayers.Contains(PlayerIdManager.LocalID) && infWin) {
				// Infected win, player was infected
				floor = 20;
				roof = 135;

			} else if (!infectedPlayers.Contains(PlayerIdManager.LocalId) && !infWin){
				// Survivor win, player was not infected
				floor = 50;
				roof = 200;

			} else {
				// Survivor win, player was infected
				floor = 20;
				roof = 100;
			}
			reward = Random.Range(floor, roof);
			FusionNotifier.Send(new FusionNotification()
                {
                    title = "You won " + reward + " bits!",
                    showTitleOnPopup = true,
                    popupLength = 3f,
                    isMenuItem = false,
                    isPopup = true,
                });
		}

		protected override void OnUpdate() 
		{
			// Active update
			if (IsActive()) 
			{
				foreach (var keyPair in TeamLogoInstances)
				{
					keyPair.Value.Update();
				}
				  
				if (NetworkInfo.IsServer)
				{
					if (stopwatch.ElapsedMilliseconds >= minutes * 60000)
					{
						gameOverSatisfied = true;
						infWin = false;
						stopwatch.Stop();
						StopGamemode();
					}
				}
			}
		}

		// Logic for getting the infected inf key for players.
		protected string GetInfKey(PlayerId id) {
			if (id == null)
				return "";

			return $"{PlayerInfKey}.{id.LongId}";
		}
		
		public static void SendNotificationPublic(FusionNotification notification)
		{
			Type type = FusionMod.FusionAssembly.GetTypes()
				.First(t => t.Name == "FusionNotifier");

			type.GetMethod("Send", BindingFlags.Static | BindingFlags.Public)
				.Invoke(null, new[] { notification });
		}
	}
}