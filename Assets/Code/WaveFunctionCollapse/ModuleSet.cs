using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

/// <summary>
/// Represents a specialized collection tailored for the `Module` type,
/// providing efficient operations and enhanced functionalities.
/// </summary>
[System.Serializable]
public class ModuleSet : ICollection<Module> {
	/// <summary>
	/// The number of bits used per item in the internal representation.
	/// </summary>
	private const int bitsPerItem = 64;

	/// <summary>
	/// Compact bitset representation of the collection.
	/// Each module corresponds to a bit in this array.
	/// </summary>
	[SerializeField]
	private long[] data;

	
	/// <summary>
	/// Cached entropy value of the module set.
	/// </summary>
	private float entropy;
	
	/// <summary>
	/// Indicates if the cached entropy value is outdated and needs to be recalculated.
	/// </summary>
	private bool entropyOutdated = true;

	/// <summary>
	/// Gets the total number of modules in the set.
	/// </summary>
	public int Count {
		get {
			int result = 0;
			for (int i = 0; i < this.data.Length - 1; i++) {
				result += countBits(this.data[i]);
			}
			return result + countBits(this.data[this.data.Length - 1] & this.lastItemUsageMask);
		}
	}

	/// <summary>
	/// Determines the usage mask for the last item in the internal data representation.
	/// </summary>
	private long lastItemUsageMask {
		get {
			return ((long)1 << (ModuleData.Current.Length % 64)) - 1;
		}
	}

	/// <summary>
	/// Checks if all possible modules are present in the set.
	/// </summary>
	public bool Full {
		get {
			for (int i = 0; i < this.data.Length - 1; i++) {
				if (this.data[i] != ~0) {
					return false;
				}
			}
			return (~this.data[this.data.Length - 1] & this.lastItemUsageMask) == 0;
		}
	}

	/// <summary>
	/// Checks if the set is empty.
	/// </summary>
	public bool Empty {
		get {
			for (int i = 0; i < this.data.Length - 1; i++) {
				if (this.data[i] != 0) {
					return false;
				}
			}
			return (this.data[this.data.Length - 1] & this.lastItemUsageMask) == 0;
		}
	}

	/// <summary>
	/// Gets the entropy value of the module set, indicating its randomness or uncertainty.
	/// </summary>
	public float Entropy {
		get {
			if (this.entropyOutdated) {
				this.entropy = this.calculateEntropy();
				this.entropyOutdated = false;
			}
			return this.entropy;
		}
	}
	
	/// <summary>
	/// Initializes a new instance of the ModuleSet, optionally filling it with all modules.
	/// </summary>
	public ModuleSet(bool initializeFull = false) {
		this.data = new long[ModuleData.Current.Length / bitsPerItem + (ModuleData.Current.Length % bitsPerItem == 0 ? 0 : 1)];
		
		if (initializeFull) {
			for (int i = 0; i < this.data.Length; i++) {
				this.data[i] = ~0;
			}
		}
	}

	/// <summary>
	/// Initializes a new instance of the ModuleSet with a source collection of modules.
	/// </summary>
	public ModuleSet(IEnumerable<Module> source) : this() {
		foreach (var module in source) {
			this.Add(module);
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ModuleSet"/> class by copying data from an existing instance.
	/// </summary>
	/// <param name="source">The source <see cref="ModuleSet"/> instance to copy data from.</param>
	public ModuleSet(ModuleSet source) {
		this.data = source.data.ToArray();
		this.entropy = source.Entropy;
		this.entropyOutdated = false;
	}

	/// <summary>
	/// Creates a new ModuleSet from a collection of Module objects.
	/// </summary>
	/// <param name="source">An <code>IEnumerableModule</code> collection to be converted into a ModuleSet.</param>
	/// <returns>A new ModuleSet containing all the Module objects from the source collection.</returns>
	public static ModuleSet FromEnumerable(IEnumerable<Module> source) {
		var result = new ModuleSet();
		foreach (var module in source) {
			result.Add(module);
		}
		return result;
	}

	/// <summary>
	/// Adds a module to the set.
	/// </summary>
	public void Add(Module module) {
		int i = module.Index / bitsPerItem;
		long mask = (long)1 << (module.Index % bitsPerItem);

		long value = this.data[i];
	
		if ((value & mask) == 0) {
			this.data[i] = value | mask;
			this.entropyOutdated = true;
		}
	}

	/// <summary>
	/// Removes a module from the set.
	/// </summary>
	public bool Remove(Module module) {
		int i = module.Index / bitsPerItem;
		long mask = (long)1 << (module.Index % bitsPerItem);

		long value = this.data[i];
	
		if ((value & mask) != 0) {
			this.data[i] = value & ~mask;
			this.entropyOutdated = true;
			return true;
		} else {
			return false;
		}
	}

	/// <summary>
	/// Determines whether the module is present in the set.
	/// </summary>
	public bool Contains(Module module) {
		int i = module.Index / bitsPerItem;
		long mask = (long)1 << (module.Index % bitsPerItem);
		return (this.data[i] & mask) != 0;
	}

	/// <summary>
	/// Determines whether a module with the specified index is present in the set.
	/// </summary>
	public bool Contains(int index) {
		int i = index / bitsPerItem;
		long mask = (long)1 << (index % bitsPerItem);
		return (this.data[i] & mask) != 0;
	}

	/// <summary>
	/// Removes all modules from the set.
	/// </summary>
	public void Clear() {
		this.entropyOutdated = true;
		for (int i = 0; i < this.data.Length; i++) {
			this.data[i] = 0;
		}
	}

	/// <summary>
	/// Removes all modules that are not in the supplied set.
	/// </summary>
	/// <param name="moduleSet"></param>
	/// <returns></returns>
	
	public void Intersect(ModuleSet moduleSet) {
		for (int i = 0; i < this.data.Length; i++) {
			long current = this.data[i];
			long mask = moduleSet.data[i];
			long updated = current & mask;

			if (current != updated) {
				this.data[i] = updated;
				this.entropyOutdated = true;
			}
		}
	}

	/// <summary>
	/// Adds all modules from another set to this set.
	/// </summary>
	public void Add(ModuleSet set) {
		for (int i = 0; i < this.data.Length; i++) {
			long current = this.data[i];
			long updated = current | set.data[i];

			if (current != updated) {
				this.data[i] = updated;
				this.entropyOutdated = true;
			}
		}
	}

	/// <summary>
	/// Removes all modules present in another set from this set.
	/// </summary>
	public void Remove(ModuleSet set) {
		for (int i = 0; i < this.data.Length; i++) {
			long current = this.data[i];
			long updated = current & ~set.data[i];

			if (current != updated) {
				this.data[i] = updated;
				this.entropyOutdated = true;
			}
		}
	}

	/// <summary>
	/// Counts the number of bits set to 1 in the long value. <br></br>
	/// From: https://stackoverflow.com/a/2709523/895589
	/// </summary>
	private static int countBits(long i) {
		i = i - ((i >> 1) & 0x5555555555555555);
		i = (i & 0x3333333333333333) + ((i >> 2) & 0x3333333333333333);
		return (int)((((i + (i >> 4)) & 0xF0F0F0F0F0F0F0F) * 0x101010101010101) >> 56);
	}

	/// <summary>
	/// Gets a value indicating whether the module set is read-only.
	/// </summary>
	public bool IsReadOnly {
		get {
			return false;
		}
	}

	/// <summary>
	/// Copies the modules of the set to an array, starting at a particular index.
	/// </summary>
	public void CopyTo(Module[] array, int arrayIndex) {
		foreach (var item in this) {
			array[arrayIndex] = item;
			arrayIndex++;
		}
	}

	/// <summary>
	/// Returns an enumerator that iterates through the set.
	/// </summary>
	public IEnumerator<Module> GetEnumerator() {
		int index = 0;
		for (int i = 0; i < this.data.Length; i++) {
			long value = this.data[i];
			if (value == 0) {
				index += bitsPerItem;
				continue;
			}
			for (int j = 0; j < bitsPerItem; j++) {
				if ((value & ((long)1 << j)) != 0) {
					yield return ModuleData.Current[index];
				}
				index++;
				if (index >= ModuleData.Current.Length) {
					yield break;
				}
			}
		}
	}

	/// <summary>
	/// Returns an enumerator that iterates through the set.
	/// </summary>
	IEnumerator IEnumerable.GetEnumerator() {
		return (IEnumerator)this.GetEnumerator();
	}

	/// <summary>
	/// Calculates the entropy value of the set based on the modules' probabilities.
	/// </summary>
	private float calculateEntropy() {
		float total = 0;
		float entropySum = 0;
		foreach (var module in this) {
			total += module.Prototype.Probability;
			entropySum += module.PLogP;
		}
		return -1f / total * entropySum + Mathf.Log(total);
	}
}
