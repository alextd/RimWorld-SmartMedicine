using System;
using System.Collections.Generic;
using Verse;

namespace TD.Utilities
{
	/*
	 A Dictionary that is easily IExposable!
	 Put Dictionaries in Dictionaries.
	 Save/load them easily.
	 
	ExDictionary<K, V> d = new ExDictionary<K, V>()
	ExDictionary<K, ExDictionary<A, B>> d = new ExDictionary<K, ExDictionary<A, B>>()

		Be sure to call d.ExposeData() in your ExposeData()

		It's not a subclass of Dictionary, but it does implicitly cast:

	Dictionary<K,V> realDict = d;
	public Dictionary<K,V> Get(ExDictionary<K,V> exD) => exD;
	
		Name them if you use multiple so the xml tag doesn't conflict:

	ExDictionary<K, V> d1 = new ExDictionary<K, V>("name1")
	ExDictionary<K, V> d2 = new ExDictionary<K, V>("name2") 

		ExDictionary assumes loading by LookMode.Reference when the Type is ILoadReferencable.
		But if your ExDictionary is the primary storage for its contents, loading needs to be Deep:
		e.g. if V is IExposable and ILoadReferencable:
	
	ExDictionary<K, V> d = new ExDictionary<K, V>() {valMode = LookMode.Deep} 
	
	*/
	public class ExDictionary<K, V> : IExposable where K : new()
	{
		private Dictionary<K, V> intDict = new Dictionary<K, V>();

		public string exposeString = "dict";
		public LookMode keyMode = LookMode.Undefined;
		public LookMode valMode = LookMode.Undefined;
		
		public ExDictionary(Dictionary<K, V> otherDict) : this()
		{
			intDict = new Dictionary<K, V>(otherDict);
		}
		public ExDictionary(string s) : this()
		{
			exposeString = s;
		}
		public ExDictionary()
		{
			if (typeof(ILoadReferenceable).IsAssignableFrom(typeof(K)))
			{
#if DEBUG
				Log.Warning($"Assuming ExposeableDictionary key type {typeof(K)} to LookMode.Reference");
#endif
				keyMode = LookMode.Reference;  //We are assuming, set Deep if needed
			}
			if (typeof(ILoadReferenceable).IsAssignableFrom(typeof(V)))
			{
#if DEBUG
				Log.Warning($"Assuming ExposeableDictionary value type {typeof(V)} to LookMode.Reference");
#endif
				valMode = LookMode.Reference;  //We are assuming, set Deep if needed
			}
		}

		//LookMode.Reference and Load by value happens in two different steps so key/value lists need to be saved between them
		private List<K> keysTemp;
		private List<V> valsTemp;
		public virtual void ExposeData()
		{
			Scribe_Collections.Look<K, V>(ref intDict, exposeString, keyMode, valMode, ref keysTemp, ref valsTemp);
		}

		public static implicit operator Dictionary<K, V>(ExDictionary<K, V> dc)
		{
			return dc.intDict;
		}
	}
}
