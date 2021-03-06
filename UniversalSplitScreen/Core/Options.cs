﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace UniversalSplitScreen.Core
{
	internal class Options
	{
		private static readonly List<OptionsStructure> options = new List<OptionsStructure>();
		public static OptionsStructure CurrentOptions { get; private set; } = new OptionsStructure();

		public static void LoadOptions()
		{
			CurrentOptions = CurrentOptions ?? new OptionsStructure();
			options.Add(CurrentOptions);//Default

			var dInfo = new DirectoryInfo(GetConfigFolder());

			foreach (FileInfo file in dInfo.GetFiles("*.json"))
			{
				if (ReadFromFile(file.FullName, out OptionsStructure o))
				{
					options.Add(o);
					Logger.WriteLine($"Loaded {file.Name} : {o.OptionsName}");
				}
			}

			CurrentOptions = options[0];

			ComboBox comboBox = Program.Form.OptionsComboBox;
			var array = options.ToArray();
			comboBox.Items.AddRange(array);
			comboBox.SelectedItem = CurrentOptions;
		}
		
		//The form's checkboxes need to know the field names in OptionsStructure so they can update them via reflection
		public static void LoadButtonClicked()
		{
			CurrentOptions = (OptionsStructure)Program.Form.OptionsComboBox.SelectedItem;
			Program.Form.PopulateOptionsRefTypes(CurrentOptions);
		}

		public static void SaveButtonClicked()
		{
			WriteToFile(CurrentOptions);
		}

		public static void NewButtonClicked(string name)
		{
			CurrentOptions = CurrentOptions.Clone();
			CurrentOptions.OptionsName = name;
			options.Add(CurrentOptions);

			ComboBox cb = Program.Form.OptionsComboBox;
			cb.Items.Add(CurrentOptions);
			cb.SelectedItem = CurrentOptions;
		}

		public static void DeleteButtonClicked()
		{
			if (UI.Prompt.ShowOkCancelDialog("Delete?") == System.Windows.Forms.DialogResult.OK)
			{
				ComboBox cb = Program.Form.OptionsComboBox;
				var toDelete = (OptionsStructure)cb.SelectedItem;
				DeleteFile(toDelete);

				if (cb.Items.Count > 1 && cb.Items.Contains(toDelete))
				{
					cb.Items.Remove(toDelete);
					cb.SelectedItem = cb.Items[0];
				}
			}
		}
		
		private static bool WriteToFile(OptionsStructure options)
		{
			try
			{
				string directory = GetConfigFolder();
				Directory.CreateDirectory(directory);

				using (StreamWriter file = File.CreateText(Path.Combine(directory, options.OptionsName + ".json")))
				{
					var serializer = new JsonSerializer
					{
						Formatting = Formatting.Indented
					};
					serializer.Serialize(file, options);
				}

				return true;
			}
			catch (Exception e)
			{
				Logger.WriteLine($"Error writing options to file: {e}");
				return false;
			}
		}
		
		private static  bool ReadFromFile(string path, out OptionsStructure options)
		{
			try
			{
				using (StreamReader file = File.OpenText(path))
				{
					var serializer = new JsonSerializer();
					options = (OptionsStructure)serializer.Deserialize(file, typeof(OptionsStructure));
					return true;
				}
			}
			catch (Exception e)
			{
				Logger.WriteLine($"Error reading options from a file: {e}");
				options = null;
				return false;
			}
		}

		private static bool DeleteFile(OptionsStructure options)
		{
			try
			{
				string path = Path.Combine(GetConfigFolder(), options.OptionsName + ".json");
				Logger.WriteLine($"Deleting {path}");
				File.Delete(path);
				return true;
			}
			catch (Exception e)
			{
				Logger.WriteLine($"Error deleting options file: {e}");
				return false;
			}
		}

		private static string GetConfigFolder() => Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "config");
	}
}
