﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmartMedicine
{
	static class Log
	{
		[System.Diagnostics.Conditional("DEBUG")]
		public static void Message(string x)
		{
			Verse.Log.Message(x);
		}
	}
}
