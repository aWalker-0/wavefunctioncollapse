using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor;
using System;

/// <summary>
/// From what I understand here, this is the basic, abstract class that defines the look of the other two 'map' type
/// classes, <see cref="InfiniteMap"/>  and <see cref="TilingMap"/>.
/// </summary>
public abstract class AbstractMap {
	public const float BLOCK_SIZE = 2f;
	public const int HISTORY_SIZE = 3000;

	public static System.Random Random;

	public readonly RingBuffer<HistoryItem> History;
	
	/// <summary>
	/// Represents a queue of modules that need to be removed from specific slots.
	/// <remarks>
	/// This queue is utilized during the collapsing process, particularly when certain 
	/// modules in slots become invalid due to constraints or other influencing factors.
	/// Modules queued in the `RemovalQueue` are set to be removed from their respective
	/// slots to ensure the validity and consistency of the map generation process.
	/// <br></br> 
	/// The removals in this queue can be cleared during various stages of the collapse
	/// operation, especially during backtracking, to ensure that the algorithm does not 
	/// mistakenly apply outdated removal decisions.
	/// <br></br>
	/// Each entry in the `RemovalQueue` maps a slot's position (Vector3Int) to a set of 
	/// modules (`ModuleSet`) that should be removed from that position.
	/// </remarks>
	/// </summary>
	public readonly QueueDictionary<Vector3Int, ModuleSet> RemovalQueue;
	/// <summary>
	/// Represents a collection of slots that are yet to be collapsed.
	/// 
	/// <remarks>
	/// This HashSet dynamically tracks the slots that are currently being 
	/// processed or evaluated. As slots are collapsed and their final 
	/// states are determined, they are removed from this collection. 
	/// However, during backtracking or undo operations, slots can be 
	/// re-added to the workArea, indicating they need to be re-evaluated.
	/// <br></br>
	/// By maintaining this dynamic working set, the algorithm efficiently 
	/// focuses its efforts on unresolved parts of the map, aiding in 
	/// targeted map generation.
	/// </remarks>
	/// </summary>
	private HashSet<Slot> workArea;
	public readonly Queue<Slot> BuildQueue;

	private int backtrackBarrier;
	private int backtrackAmount = 0;

	public readonly short[][] InitialModuleHealth;

	public AbstractMap() {
		// ? - Why define the "Random" variable of this concrete class?
		InfiniteMap.Random = new System.Random();

		this.History = new RingBuffer<HistoryItem>(AbstractMap.HISTORY_SIZE);
		this.History.OnOverflow = item => item.Slot.Forget();
		this.RemovalQueue = new QueueDictionary<Vector3Int, ModuleSet>(() => new ModuleSet());
		this.BuildQueue = new Queue<Slot>();

		this.InitialModuleHealth = this.createInitialModuleHealth(ModuleData.Current);

		this.backtrackBarrier = 0;
	}

	public abstract Slot GetSlot(Vector3Int position);

	public abstract IEnumerable<Slot> GetAllSlots();

	public abstract void ApplyBoundaryConstraints(IEnumerable<BoundaryConstraint> constraints);	

    
    /*
     * "`NotifySlotCollapsed and NotifySlotCollapseUndone: Methods for notifying the map about slot state changes."
     * 
     * I think the "Slot" class represents an empty partition in an "uncollapsed" state.
     * I also think that `workArea` is the variable that we store all of the places not yet "collapsed", and the type
     * definition supports this thinking, since it is a Collection of Slots.
     */
    
	public void NotifySlotCollapsed(Slot slot) {
		if (this.workArea != null) {
			this.workArea.Remove(slot);
		}
		this.BuildQueue.Enqueue(slot);
	}

	/// <summary>
	/// Adds a given <see cref="Slot"/> back to the `workArea`.
	/// </summary>
	/// <param name="slot"></param>
	public void NotifySlotCollapseUndone(Slot slot) {
		if (this.workArea != null) {
			this.workArea.Add(slot);
		}
	}
	
	
	
	public void FinishRemovalQueue() {
		while (this.RemovalQueue.Any()) {
			var kvp = this.RemovalQueue.Dequeue();
			var slot = this.GetSlot(kvp.Key);
			if (!slot.Collapsed) {
				slot.RemoveModules(kvp.Value, false);
			}
		}
	}

	public void EnforceWalkway(Vector3Int start, int direction) {
		var slot = this.GetSlot(start);
		var toRemove = slot.Modules.Where(module => !module.GetFace(direction).Walkable);
		slot.RemoveModules(ModuleSet.FromEnumerable(toRemove));
	}

	public void EnforceWalkway(Vector3Int start, Vector3Int destination) {
		int direction = Orientations.GetIndex((Vector3)(destination - start));
		this.EnforceWalkway(start, direction);
		this.EnforceWalkway(destination, (direction + 3) % 6);
	}

	
	/*
	 * If I understand this correctly, this is the main function that executes the 'WFC' algorithm. Note that the `Slot`
	 * class also has a `Collapse` method, but it's function seems more to set it's `Module` field and notify the map
	 * that it has collapsed. (Also there's this weird 'Remove' logic that happens, which I don't understand.)
	 */
	public void Collapse(IEnumerable<Vector3Int> targets, bool showProgress = false) {
#if UNITY_EDITOR
		try {
#endif
			/*
			 * Wipe the `RemovalQueue` var (I think because we don't want to be removing while we are also collapsing?
			 * I have no idea - I need to understand this RemovalQueue more.
			 */
			this.RemovalQueue.Clear();
			
			// Define `workArea` with new HashSet of empty slots built from a provided collection of Vec3Ints
			/*
			 * Define a new 'queue' of work to be done using the WFC algorithm; the 'input' area to be collapsed,
			 * comprised of empty `Slot`s that need to have `Module`s (3D model sections) slotted in.
			 */
			this.workArea = new HashSet<Slot>(targets
				// Take each `target` in `targets`, and create a `Slot` from it
				.Select(target => this.GetSlot(target))
				// Only return(?) the `Slot` if not null and isn't collapsed
				.Where(slot => slot != null && !slot.Collapsed));

			// Loop until all `Slot`s are collapsed inside the work area 
			while (this.workArea.Any()) {
				// Define float var that is infinitely positive (to make things easy once we get to the foreach loop)
				float minEntropy = float.PositiveInfinity;
				// Define empty `Slot` var
				Slot selected = null;

				// 
				// Loop through each slot in workArea (this is the section in which we find the lowest entropy Slot)
				foreach (var slot in workArea) {
					// Extract the entropy of the slot
					float entropy = slot.Modules.Entropy;
					/*
					 * If this slot's entropy is less than the previously found lowest entropy slot, make this slot the 
					 * new lowest entropy slot (which is stored in 'selected').
					 */
					if (entropy < minEntropy) {
						selected = slot;
						minEntropy = entropy;
					}
				}
				
				/*
				 * After finding the lowest entropy slot, try to collapse it randomly (and by 'randomly' I think it
				 * means: "put a random `Module` in that works", with a `Module` representing a piece of the physical
				 * city; a 3D model section).
				 */
				try {
					selected.CollapseRandom();
				}
				/*
				 * If we encounter a `CollapseFailedException` (which is expected since there will be times when we fail
				 * to collapse due to there not being any module that fits in a given slot) do the following(?)
				 */
				catch (CollapseFailedException) {
					// Clear the RemovalQueue (I still don't know why)
					this.RemovalQueue.Clear();
					if (this.History.TotalCount > this.backtrackBarrier) {
						this.backtrackBarrier = this.History.TotalCount;
						this.backtrackAmount = 2;
					} else {
						this.backtrackAmount *= 2;
					}
					if (this.backtrackAmount > 0) {
						Debug.Log(this.History.Count + " Backtracking " + this.backtrackAmount + " steps...");
					}
					this.Undo(this.backtrackAmount);
				}

#if UNITY_EDITOR
				if (showProgress && this.workArea.Count % 20 == 0) {
					if (EditorUtility.DisplayCancelableProgressBar("Collapsing area... ", this.workArea.Count + " left...", 1f - (float)this.workArea.Count() / targets.Count())) {
						EditorUtility.ClearProgressBar();
						throw new Exception("Map generation cancelled.");
					}
				}
#endif
			}

#if UNITY_EDITOR
			if (showProgress) {
				EditorUtility.ClearProgressBar();
			}
		}
		catch (Exception exception) {
			if (showProgress) {
				EditorUtility.ClearProgressBar();
			}
			Debug.LogWarning("Exception in world generation thread at" + exception.StackTrace);
			throw exception;
		}
#endif
	}

	// -> WTF "Collapse" is overloaded??
	/*
	 * It is overloaded because the first method definition expects a collection of Vec3Ints, while this method call
	 * expects a starting place, and the size of what is to be collapsed. The starting point being a point (or maybe
	 * section of a grid) in 3D spaceI think, and size being the width, height, and depth (think of a cube) to be
	 * collapsed.
	 */
	public void Collapse(Vector3Int start, Vector3Int size, bool showProgress = false) {
		var targets = new List<Vector3Int>();
		for (int x = 0; x < size.x; x++) {
			for (int y = 0; y < size.y; y++) {
				for (int z = 0; z < size.z; z++) {
					targets.Add(start + new Vector3Int(x, y, z));
				}
			}
		}
		this.Collapse(targets, showProgress);
	}

	public void Undo(int steps) {
		while (steps > 0 && this.History.Any()) {
			var item = this.History.Pop();

			foreach (var slotAddress in item.RemovedModules.Keys) {
				this.GetSlot(slotAddress).AddModules(item.RemovedModules[slotAddress]);
			}

			item.Slot.Module = null;
			this.NotifySlotCollapseUndone(item.Slot);
			steps--;
		}
		if (this.History.Count == 0) {
			this.backtrackBarrier = 0;
		}
	}

	private short[][] createInitialModuleHealth(Module[] modules) {
		var initialModuleHealth = new short[6][];
		for (int i = 0; i < 6; i++) {
			initialModuleHealth[i] = new short[modules.Length];
			foreach (var module in modules) {
				foreach (var possibleNeighbor in module.PossibleNeighbors[(i + 3) % 6]) {
					initialModuleHealth[i][possibleNeighbor.Index]++;
				}
			}
		}
		
#if UNITY_EDITOR
		for (int i = 0; i < modules.Length; i++) {
			for (int d = 0; d < 6; d++) {
				if (initialModuleHealth[d][i] == 0) {
					Debug.LogError("Module " + modules[i].Name + " cannot be reached from direction " + d + " (" + modules[i].GetFace(d).ToString() + ")!", modules[i].Prefab);
					throw new Exception("Unreachable module.");
				}
			}
		}
#endif

		return initialModuleHealth;
	}

	public short[][] CopyInititalModuleHealth() {
		return this.InitialModuleHealth.Select(a => a.ToArray()).ToArray();
	}
}
