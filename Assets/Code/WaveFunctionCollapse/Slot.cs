using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Slot {
	/// <summary>
	/// Represents the 3D position of the slot.
	/// </summary>
	public Vector3Int Position;

	// List of modules that can still be placed here
	// (defined in the constructor)
	public ModuleSet Modules;

	// Direction -> Module -> Number of items in this.getneighbor(direction).Modules that allow this module as a neighbor
	/// <summary>
	/// 
	/// </summary>
	public short[][] ModuleHealth;

	// Reference to the map class
	private AbstractMap map;
	
	/// <summary>
	/// The Module placed inside this slot.
	/// <remarks>
	/// If not null, then this means the slot has been collapsed, a module placed inside.
	/// </remarks>
	/// </summary>
	public Module Module;

	public GameObject GameObject;

	public bool Collapsed {
		get {
			return this.Module != null;
		}
	}

	/*
	 * Looks to be only used in the 'Tree Placer' logic, which (I think) just places a tree on this `Slot`'s `Module`
	 * when the `Module` has been placed in the game world.
	 */
	public bool ConstructionComplete {
		get {
			return this.GameObject != null || (this.Collapsed && !this.Module.Prototype.Spawn);
		}
	}

	public Slot(Vector3Int position, AbstractMap map) {
		// Define this Slot's position in space/the grid
		this.Position = position;
		// Define the map (which the Slot exists in)
		this.map = map;
		// 
		this.ModuleHealth = map.CopyInititalModuleHealth();
		this.Modules = new ModuleSet(initializeFull: true);
	}

	public Slot(Vector3Int position, AbstractMap map, Slot prototype) {
		this.Position = position;
		this.map = map;
		this.ModuleHealth = prototype.ModuleHealth.Select(a => a.ToArray()).ToArray();
		this.Modules = new ModuleSet(prototype.Modules);
	}

	/// <summary>
	/// Returns a neighboring `Slot` in the provided direction.
	/// </summary>
	/// <param name="direction"></param>
	/// <returns></returns>
	// TODO only look up once and then cache???
	public Slot GetNeighbor(int direction) {
		return this.map.GetSlot(this.Position + Orientations.Direction[direction]);
	}

	public void Collapse(Module module) {
		// Check if this slot already has a `Module` assigned/placed within it
		if (this.Collapsed) {
			Debug.LogWarning("Trying to collapse already collapsed slot.");
			return;
		}
	
		/*
		 * What is this HistoryItem? It takes a `Slot` as a param which it stores as a field inside the class... It also
		 * instantiates another field `RemovedModules`, which is a Dictionary (Vector3Int to ModuleSet). This same field
		 * is accessed down later in this class in `RemoveModules` as well, so there's some connection here.
		 */
		this.map.History.Push(new HistoryItem(this));
		
		// Assign this slot's `Module` to the one provided from the method param 
		this.Module = module;
		/*
		 * Define local var and create a copied instance of this slot's `Modules` field (note that this includes the
		 * just-assigned `Module` which now occupied this slot.
		 */
		var toRemove = new ModuleSet(this.Modules);
		// Subtract the `Module` we just assigned to the slot (so that it doesn't get removed as well)
		toRemove.Remove(module);
		// Remove all other modules, except the one that occupies this slot
		this.RemoveModules(toRemove);
		
		// Notify the map that this slot is now collapsed!
		this.map.NotifySlotCollapsed(this);
	}
	
	private void checkConsistency(Module module) {
		for (int d = 0; d < 6; d++) {
			if (this.GetNeighbor(d) != null && this.GetNeighbor(d).Collapsed && !this.GetNeighbor(d).Module.PossibleNeighbors[(d + 3) % 6].Contains(module)) {
				throw new Exception("Illegal collapse, not in neighbour list. (Incompatible connectors)");
			}
		}

		if (!this.Modules.Contains(module)) {
			throw new Exception("Illegal collapse!");
		}
	}

	public void CollapseRandom() {
		if (!this.Modules.Any()) {
			throw new CollapseFailedException(this);
		}
		if (this.Collapsed) {
			throw new Exception("Slot is already collapsed.");
		}
		
		float max = this.Modules.Select(module => module.Prototype.Probability).Sum();
		float roll = (float)(InfiniteMap.Random.NextDouble() * max);
		float p = 0;
		foreach (var candidate in this.Modules) {
			p += candidate.Prototype.Probability;
			if (p >= roll) {
				this.Collapse(candidate);
				return;
			}
		}
		this.Collapse(this.Modules.First());
	}

	// This modifies the supplied ModuleSet as a side effect
	public void RemoveModules(ModuleSet modulesToRemove, bool recursive = true) {
		modulesToRemove.Intersect(this.Modules);

		if (this.map.History != null && this.map.History.Any()) {
			var item = this.map.History.Peek();
			if (!item.RemovedModules.ContainsKey(this.Position)) {
				item.RemovedModules[this.Position] = new ModuleSet();
			}
			item.RemovedModules[this.Position].Add(modulesToRemove);
		}

		for (int d = 0; d < 6; d++) {
			int inverseDirection = (d + 3) % 6;
			var neighbor = this.GetNeighbor(d);
			if (neighbor == null || neighbor.Forgotten) {
#if UNITY_EDITOR
				if (this.map is InfiniteMap && (this.map as InfiniteMap).IsOutsideOfRangeLimit(this.Position + Orientations.Direction[d])) {
					(this.map as InfiniteMap).OnHitRangeLimit(this.Position + Orientations.Direction[d], modulesToRemove);
				}
#endif
				continue;
			}

			foreach (var module in modulesToRemove) {
				for (int i = 0; i < module.PossibleNeighborsArray[d].Length; i++) {
					var possibleNeighbor = module.PossibleNeighborsArray[d][i];
					if (neighbor.ModuleHealth[inverseDirection][possibleNeighbor.Index] == 1 && neighbor.Modules.Contains(possibleNeighbor)) {
						this.map.RemovalQueue[neighbor.Position].Add(possibleNeighbor);
					}
#if UNITY_EDITOR
					if (neighbor.ModuleHealth[inverseDirection][possibleNeighbor.Index] < 1) {
						throw new System.InvalidOperationException("ModuleHealth must not be negative. " + this.Position + " d: " + d);
					}
#endif
					neighbor.ModuleHealth[inverseDirection][possibleNeighbor.Index]--;
				}
			}
		}

		this.Modules.Remove(modulesToRemove);

		if (this.Modules.Empty) {
			throw new CollapseFailedException(this);
		}

		if (recursive) {
			this.map.FinishRemovalQueue();
		}
	}

	/// <summary>
	/// Add modules non-recursively.
	/// Returns true if this lead to this slot changing from collapsed to not collapsed.
	/// </summary>
	public void AddModules(ModuleSet modulesToAdd) {
		foreach (var module in modulesToAdd) {
			if (this.Modules.Contains(module) || module == this.Module) {
				continue;
			}
			for (int d = 0; d < 6; d++) {
				int inverseDirection = (d + 3) % 6;
				var neighbor = this.GetNeighbor(d);
				if (neighbor == null || neighbor.Forgotten) {
					continue;
				}

				foreach (var possibleNeighbor in module.PossibleNeighbors[d]) {
					neighbor.ModuleHealth[inverseDirection][possibleNeighbor.Index]++;
				}
			}
			this.Modules.Add(module);
		}

		if (this.Collapsed && !this.Modules.Empty) {
			this.Module = null;
			this.map.NotifySlotCollapseUndone(this);
		}
	}

	public void EnforceConnector(int direction, int connector) {
		var toRemove = this.Modules.Where(module => !module.Fits(direction, connector));
		this.RemoveModules(ModuleSet.FromEnumerable(toRemove));
	}

	public void ExcludeConnector(int direction, int connector) {
		var toRemove = this.Modules.Where(module => module.Fits(direction, connector));
		this.RemoveModules(ModuleSet.FromEnumerable(toRemove));
	}

	public override int GetHashCode() {
		return this.Position.GetHashCode();
	}

	public void Forget() {
		this.ModuleHealth = null;
		this.Modules = null;
	}

	public bool Forgotten {
		get {
			return this.Modules == null;
		}
	}
}
