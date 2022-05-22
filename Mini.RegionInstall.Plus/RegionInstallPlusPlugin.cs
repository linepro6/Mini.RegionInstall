// <copyright file="RegionInstallPlusPlugin.cs" company="linepro6">
// This file is part of Mini.RegionInstaller.Plus.
//
// Mini.RegionInstaller.Plus is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Mini.RegionInstaller.Plus is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Mini.RegionInstaller.Plus.  If not, see https://www.gnu.org/licenses/
// </copyright>

namespace Mini.RegionInstall.Plus
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Net.Http;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using BepInEx;
	using BepInEx.Configuration;
	using BepInEx.IL2CPP;
#if REACTOR
	using Reactor;
#endif

	/**
	 * <summary>
	 * Plugin that installs user specified servers into the region file.
	 * </summary>
	 */
	[BepInAutoPlugin("com.linepro6.regioninstall.plus")]
	[BepInProcess("Among Us.exe")]
#if REACTOR
	[ReactorPluginSide(PluginSide.ClientOnly)]
#endif
	public partial class RegionInstallPlusPlugin : BasePlugin
	{
		/**
		 * <summary>
		 * Load the plugin and install the servers.
		 * </summary>
		 */
		public override void Load()
		{
			ConfigEntry<string>? regions = this.Config.Bind(
				"General",
				"Regions",
				string.Empty,
				"Create an array of regions you want to add/update. To create this array, go to https://impostor.github.io/Impostor/ and put the Regions array from the server file in here. Support HTTP GET if it startswith \"http\".");

			ConfigEntry<string>? keptRegions = this.Config.Bind(
				"General",
				"KeepDefaultRegions",
				string.Empty,
				"Comma-seperated list of region names that should be kept.");

			if (regions.Value != string.Empty)
			{
				this.Log.LogInfo("Starting Mini.RegionInstall.Plus...");
				this.UpdateRegions(regions.Value, keptRegions.Value);
			}
			else
			{
				this.Log.LogInfo("Regions is not configured. Skipping...");
			}
		}

		// [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with \"LINQ\" expressions", Justification = "<挂起>")]
		private void UpdateRegions(string regionsConfig, string keptRegionsConfig)
		{
			var defaultRegions = ServerManager.DefaultRegions;
			var regions = new List<IRegionInfo>();

			string[] keptRegions = keptRegionsConfig.Split(",");

			foreach (var region in defaultRegions)
			{
				if (keptRegions.Contains(region.Name))
				{
					this.Log.LogInfo($"Adding Default Region \"{region.Name}\"");
					regions.Add(region);
				}
			}

			if (regionsConfig.StartsWith("http"))
			{
				var regionsUrl = regionsConfig;
				this.Log.LogInfo($"Fetching {regionsUrl}");
				regionsConfig = string.Empty;
				var retries = 0;
				while (retries < 5)
				{
					try
					{
						var client = new HttpClient();
						var response = client.GetAsync(regionsUrl).Result;
						response.EnsureSuccessStatusCode();
						regionsConfig = response.Content.ReadAsStringAsync().Result;
						break;
					}
					catch (Exception e)
					{
						this.Log.LogError($"Regions HTTP GET Error. Retrying...{retries + 1}");
						this.Log.LogError(e.ToString());
						retries += 1;
					}
				}

				if (regionsConfig == string.Empty)
				{
					this.Log.LogError("Regions HTTP GET Failed!");
					return;
				}
			}

			try
			{
				var data = this.ParseRegions(regionsConfig);
				if (data.Regions.Length == 0)
				{
					this.Log.LogInfo("There is no avaliable regions. Exiting...");
					return;
				}

				IRegionInfo? defaultChooseRegion = null;
				for (int i = 0; i < data.Regions.Length; ++i)
				{
					var region = data.Regions[i];
					if (region != null)
					{
						regions.Add(region);
						this.Log.LogInfo($"Adding User Region \"{region.Name}\" @ {region.Servers[0].Ip}:{region.Servers[0].Port}");
						if (i == data.CurrentRegionIdx)
						{
							defaultChooseRegion = region;
						}
					}
				}

				ServerManager serverMngr = DestroyableSingleton<ServerManager>.Instance;
				ServerManager.DefaultRegions = regions.ToArray();
				serverMngr.AvailableRegions = regions.ToArray();
				this.Log.LogInfo($"Installed {regions.Count} regions successfully.");
				if (defaultChooseRegion != null)
				{
					serverMngr.SetRegion(defaultChooseRegion);
					this.Log.LogInfo($"Set default region: \"{defaultChooseRegion.Name}\"");
				}
			}
			catch (Exception e)
			{
				this.Log.LogError(e.ToString());
			}
		}

		private ServerData ParseRegions(string regions)
		{
			this.Log.LogInfo($"Parsing {regions}");
			switch (regions[0])
			{
				// The entire JsonServerData
				case '{':
					this.Log.LogInfo("Loading server data");

					// Set up S.T.Json with our custom converter
					JsonSerializerOptions options = new JsonSerializerOptions();
					options.Converters.Add(new RegionInfoConverter());

					return JsonSerializer.Deserialize<ServerData>(regions, options);

				// Only the IRegionInfo array
				case '[':
					this.Log.LogInfo("Loading region array");

					// Sadly AU does not have a Generic that parses IRegionInfo[] directly, so instead we wrap the array into a JsonServerData structure.
					return this.ParseRegions($"{{\"CurrentRegionIdx\":0,\"Regions\":{regions}}}");
				default:
					this.Log.LogError("Could not detect format of configured regions");
					throw new Exception("Could not detect format of configured regions");
			}
		}

		/**
		 * <summary>
		 * Clone of the base game ServerData struct to add a constructor.
		 * </summary>
		 */
		public struct ServerData
		{
			/**
			 * <summary>
			 * Initializes a new instance of the <see cref="ServerData"/> struct.
			 * </summary>
			 * <param name="currentRegionIdx">Unused, but present in JSON.</param>
			 * <param name="regions">The regions to add.</param>
			 */
			[JsonConstructor]
			public ServerData(int currentRegionIdx, IRegionInfo[] regions)
			{
				this.CurrentRegionIdx = currentRegionIdx;
				this.Regions = regions;
			}

			/**
			 * <summary>
			 * Gets the Id of the currently selected region. Unused.
			 * </summary>
			 */
			public int CurrentRegionIdx { get; }

			/**
			 * <summary>
			 * Gets an array of regions to add.
			 * </summary>
			 */
			public IRegionInfo[] Regions { get; }
		}
	}
}
