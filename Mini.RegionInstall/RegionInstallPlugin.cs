// <copyright file="RegionInstallPlugin.cs" company="miniduikboot">
// This file is part of Mini.RegionInstaller.
//
// Mini.RegionInstaller is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Mini.RegionInstaller is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Mini.RegionInstaller.  If not, see https://www.gnu.org/licenses/
// </copyright>

namespace Mini.RegionInstall
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using System.Net.Http;
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
	[BepInAutoPlugin("at.duikbo.regioninstall")]
	[BepInProcess("Among Us.exe")]
#if REACTOR
	[ReactorPluginSide(PluginSide.ClientOnly)]
#endif
	public partial class RegionInstallPlugin : BasePlugin
	{
		/**
		 * <summary>
		 * Load the plugin and install the servers.
		 * </summary>
		 */
		public override void Load()
		{
			this.Log.LogInfo("Starting Mini.RegionInstall");
			ConfigEntry<string>? regions = this.Config.Bind(
				"General",
				"Regions",
				"{\"CurrentRegionIdx\":0,\"Regions\":[]}",
				"Create an array of regions you want to add/update. To create this array, go to https://impostor.github.io/Impostor/ and put the Regions array from the server file in here");

			ConfigEntry<string>? removeRegions = this.Config.Bind(
				"General",
				"RemoveRegions",
				string.Empty,
				"Comma-seperated list of region names that should be removed.");

			// Remove regions first in case the user accidentally also adds a region with the same name.
			if (removeRegions != null)
			{
				string[] rmRegions = removeRegions.Value.Split(",");
				this.Log.LogInfo($"Removing User Regions: \"{string.Join("\", \"", rmRegions)}\"");
				this.RemoveRegions(rmRegions);
			}

			if (regions != null && regions.Value.Length != 0)
			{
				this.Log.LogInfo("Adding User Regions");
				this.AddRegions(this.ParseRegions(regions.Value));
			}
		}

		/**
		 * <summary>
		 * Add an array of regions to AU's <see cref="ServerManager"/>.
		 * </summary>
		 */
		private void AddRegions(IRegionInfo[] regions)
		{
			ServerManager serverMngr = DestroyableSingleton<ServerManager>.Instance;
			IRegionInfo? currentRegion = serverMngr.CurrentRegion;
			this.Log.LogInfo($"Adding {regions.Length} regions");
			foreach (IRegionInfo region in regions)
			{
				if (region == null)
				{
					this.Log.LogError("Could not add region");
				}
				else
				{
					serverMngr.AddOrUpdateRegion(region);
				}
			}

			// AU remembers the previous region that was set, so we need to restore it
			if (currentRegion != null)
			{
				this.Log.LogDebug("Resetting previous region");
				serverMngr.SetRegion(currentRegion);
			}
		}

		private IRegionInfo[] ParseRegions(string regions)
		{
			if (regions.StartsWith("http")) {
				using (var client = new HttpClient())
				{
					try {
				    	var response = client.GetAsync(regions).Result;
						response.EnsureSuccessStatusCode();
						string responseBody = response.Content.ReadAsStringAsync().Result;
						regions = responseBody;
					} catch (HttpRequestException e) {
						this.Log.LogError("Regions HTTP GET Error.");
						regions = null;
					}
				}
			}
			this.Log.LogInfo($"Parsing {regions}");
			switch (regions[0])
			{
				// The entire JsonServerData
				case '{':
					this.Log.LogInfo("Loading server data");

					// Set up S.T.Json with our custom converter
					JsonSerializerOptions? options = new JsonSerializerOptions();
					options.Converters.Add(new RegionInfoConverter());

					ServerData result = JsonSerializer.Deserialize<ServerData>(regions, options);

					return result.Regions;

				// Only the IRegionInfo array
				case '[':
					this.Log.LogInfo("Loading region array");

					// Sadly AU does not have a Generic that parses IRegionInfo[] directly, so instead we wrap the array into a JsonServerData structure.
					return this.ParseRegions($"{{\"CurrentRegionIdx\":0,\"Regions\":{regions}}}");
				default:
					this.Log.LogError("Could not detect format of configured regions");
					return Array.Empty<IRegionInfo>();
			}
		}

		private void RemoveRegions(string[] regionNames)
		{
			IEnumerable<IRegionInfo> newRegions = ServerManager.Instance.AvailableRegions.Where(
				(IRegionInfo r) => Array.FindIndex(regionNames, (string name) => name.Equals(r.Name, StringComparison.OrdinalIgnoreCase)) == -1);
			ServerManager.Instance.AvailableRegions = newRegions.ToArray();
			ServerManager.DefaultRegions = newRegions.ToArray();
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
