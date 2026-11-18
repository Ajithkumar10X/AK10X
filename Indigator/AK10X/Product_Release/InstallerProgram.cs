using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("AK10X Complete Suite Setup (Protected Mode)")]
[assembly: AssemblyProduct("AK10X Indian & Global Orderflow, Market Profile, Scanner & Trading Window for NinjaTrader 8")]
[assembly: AssemblyCompany("AK10X Trading Systems")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]

namespace AK10XInstaller
{
	public sealed class SetupForm : Form
	{
		private TextBox txtNtFolder;
		private TextBox txtLog;
		private Button btnInstall;
		private Button btnExportZip;
		private Button btnUninstall;

		private static readonly string[] LegacyIndicatorWrappers = new string[]
		{
			"AK10XIndianOrderflow.cs",
			"AK10XGlobalOrderflow.cs",
			"AK10XIndiaMarketProfile.cs",
			"AK10XGlobalMarketProfile.cs",
			"AK10XMarketProfileScanner.cs",
			"AK10XOrderflow.cs"
		};

		public SetupForm()
		{
			Text = "AK10X Complete Suite — NinjaTrader 8 Protected Setup";
			Size = new Size(780, 625);
			StartPosition = FormStartPosition.CenterScreen;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			BackColor = Color.FromArgb(12, 16, 23);
			ForeColor = Color.FromArgb(230, 235, 245);
			Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

			try
			{
				Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			}
			catch { }

			Panel headerPanel = new Panel
			{
				Dock = DockStyle.Top,
				Height = 100,
				BackColor = Color.FromArgb(18, 24, 34)
			};

			PictureBox picLogo = new PictureBox
			{
				Location = new Point(18, 14),
				Size = new Size(72, 72),
				SizeMode = PictureBoxSizeMode.Zoom,
				BackColor = Color.Transparent
			};
			try
			{
				using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AK10X_Logo.png"))
				{
					if (s != null)
						picLogo.Image = Image.FromStream(s);
				}
			}
			catch { }
			headerPanel.Controls.Add(picLogo);

			Label lblTitle = new Label
			{
				Text = "AK10X Complete Trading Suite (NinjaTrader 8)",
				Font = new Font("Segoe UI", 15f, FontStyle.Bold),
				ForeColor = Color.FromArgb(0, 224, 255),
				Location = new Point(104, 14),
				AutoSize = true
			};
			headerPanel.Controls.Add(lblTitle);

			Label lblSub = new Label
			{
				Text = "AK10XIndianOrderflow • AK10XGlobalOrderflow • AK10XTradingWindow • India & Global MarketProfile • Scanner",
				Font = new Font("Segoe UI", 8.6f, FontStyle.Regular),
				ForeColor = Color.FromArgb(185, 198, 215),
				Location = new Point(106, 46),
				AutoSize = true
			};
			headerPanel.Controls.Add(lblSub);

			Label lblVer = new Label
			{
				Text = "Protected Vendor Binary Mode (AK10X.Suite.dll) • Zero NinjaScript Editor Compile Errors",
				Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
				ForeColor = Color.FromArgb(80, 230, 145),
				Location = new Point(106, 68),
				AutoSize = true
			};
			headerPanel.Controls.Add(lblVer);
			Controls.Add(headerPanel);

			Label lblFolder = new Label
			{
				Text = "NinjaTrader 8 User Data Folder:",
				Location = new Point(20, 112),
				AutoSize = true,
				Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
			};
			Controls.Add(lblFolder);

			string defaultNtDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
			txtNtFolder = new TextBox
			{
				Text = defaultNtDir,
				Location = new Point(20, 136),
				Size = new Size(615, 26),
				BackColor = Color.FromArgb(22, 28, 40),
				ForeColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle
			};
			Controls.Add(txtNtFolder);

			Button btnBrowse = new Button
			{
				Text = "Browse...",
				Location = new Point(645, 134),
				Size = new Size(96, 28),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(35, 45, 65),
				ForeColor = Color.White
			};
			btnBrowse.Click += (s, e) =>
			{
				using (FolderBrowserDialog fbd = new FolderBrowserDialog())
				{
					fbd.SelectedPath = txtNtFolder.Text;
					fbd.Description = "Select your Documents\\NinjaTrader 8 folder";
					if (fbd.ShowDialog(this) == DialogResult.OK)
						txtNtFolder.Text = fbd.SelectedPath;
				}
			};
			Controls.Add(btnBrowse);

			GroupBox grpModules = new GroupBox
			{
				Text = "Included Protected Modules (Compiled in AK10X.Suite.dll — Source Code Hidden)",
				Location = new Point(20, 170),
				Size = new Size(722, 136),
				ForeColor = Color.FromArgb(255, 200, 80)
			};

			string[] modDescriptions = new string[]
			{
				"✔  1. AK10XIndianOrderflow — Indian Footprint Orderflow (Accelpix Default + Data Feed Selector)",
				"✔  2. AK10XGlobalOrderflow — Global Footprint Orderflow (NinjaTrader Feed Default + Data Feed Selector)",
				"✔  3. AK10XTradingWindow — AK10X Trading Screen (Control Center -> New -> AK10X Trading Screen)",
				"✔  4. AK10XIndiaMarketProfile — India NSE/MCX Market Profile (Daily/Weekly/Monthly, Split/Merge, DTR vs ATR)",
				"✔  5. AK10XGlobalMarketProfile — Global 3-Session Forex/Commodity Market Profile (Asia / London / New York)",
				"✔  6. AK10XMarketProfileScanner — Real-Time Accelpix Live Market Profile Scanner (Indicator + Window)"
			};

			for (int i = 0; i < modDescriptions.Length; i++)
			{
				Label lblMod = new Label
				{
					Text = modDescriptions[i],
					Location = new Point(16, 21 + i * 18),
					AutoSize = true,
					Font = new Font("Segoe UI", 8.7f, FontStyle.Regular),
					ForeColor = Color.FromArgb(220, 230, 242)
				};
				grpModules.Controls.Add(lblMod);
			}
			Controls.Add(grpModules);

			btnInstall = new Button
			{
				Text = "Install All 6 Modules (Protected)",
				Location = new Point(20, 316),
				Size = new Size(286, 42),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(18, 145, 80),
				ForeColor = Color.White,
				Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			btnInstall.Click += OnInstallClick;
			Controls.Add(btnInstall);

			btnExportZip = new Button
			{
				Text = "Save Protected Add-On .ZIP to Desktop",
				Location = new Point(318, 316),
				Size = new Size(262, 42),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(25, 95, 148),
				ForeColor = Color.White,
				Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			btnExportZip.Click += OnExportZipClick;
			Controls.Add(btnExportZip);

			btnUninstall = new Button
			{
				Text = "Uninstall Suite",
				Location = new Point(592, 316),
				Size = new Size(150, 42),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(135, 38, 38),
				ForeColor = Color.White,
				Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			btnUninstall.Click += OnUninstallClick;
			Controls.Add(btnUninstall);

			Label lblLog = new Label
			{
				Text = "Installation Status Log:",
				Location = new Point(20, 366),
				AutoSize = true,
				Font = new Font("Segoe UI", 9f, FontStyle.Bold)
			};
			Controls.Add(lblLog);

			txtLog = new TextBox
			{
				Location = new Point(20, 388),
				Size = new Size(722, 180),
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BackColor = Color.FromArgb(9, 12, 18),
				ForeColor = Color.FromArgb(130, 238, 165),
				Font = new Font("Consolas", 9f),
				BorderStyle = BorderStyle.FixedSingle
			};
			Controls.Add(txtLog);

			AppendLog("Ready. Target NinjaTrader 8 folder: " + defaultNtDir);
			AppendLog("Mode: PROTECTED VENDOR ASSEMBLY (C# source code hidden inside AK10X.Suite.dll).");
			AppendLog("Click [Install All 6 Modules (Protected)] to deploy the complete AK10X suite.");
		}

		private void AppendLog(string msg)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action<string>(AppendLog), msg);
				return;
			}
			txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine);
		}

		private static void ExtractResourceToFile(string resourceName, string targetPath)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
			using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
			{
				if (s == null)
					throw new InvalidOperationException("Embedded resource not found: " + resourceName);
				using (FileStream fs = File.Create(targetPath))
				{
					s.CopyTo(fs);
				}
			}
		}

		private static void CleanupLegacyWrappersAndDlls(string customDir, string indDir, string addOnsDir)
		{
			try
			{
				if (Directory.Exists(customDir))
				{
					foreach (string dll in Directory.GetFiles(customDir, "AK10X.*.dll"))
					{
						if (!string.Equals(Path.GetFileName(dll), "AK10X.Suite.dll", StringComparison.OrdinalIgnoreCase))
						{
							try { File.Delete(dll); } catch { }
						}
					}
					foreach (string oldBak in Directory.GetFiles(customDir, "AK10X.Suite.dll.*"))
					{
						try { File.Delete(oldBak); } catch { }
					}
					foreach (string f in Directory.GetFiles(customDir, "INDMarketProfile*"))
					{
						try { File.Delete(f); } catch { }
					}
					string rootStacker = Path.Combine(customDir, "AK10XWindowStacker.cs");
					if (File.Exists(rootStacker)) try { File.Delete(rootStacker); } catch { }
				}

				if (Directory.Exists(indDir))
				{
					foreach (string wf in LegacyIndicatorWrappers)
					{
						string p = Path.Combine(indDir, wf);
						if (File.Exists(p)) try { File.Delete(p); } catch { }
					}

					foreach (string f in Directory.GetFiles(indDir, "INDMarketProfile*"))
					{
						try { File.Delete(f); } catch { }
					}
					foreach (string f in Directory.GetFiles(indDir, "MarketProfile*"))
					{
						try { File.Delete(f); } catch { }
					}
				}

				if (Directory.Exists(addOnsDir))
				{
					string oldAddOn = Path.Combine(addOnsDir, "AK10XWindowStacker.cs");
					if (File.Exists(oldAddOn)) try { File.Delete(oldAddOn); } catch { }
				}
			}
			catch { }
		}

		private async void OnInstallClick(object sender, EventArgs e)
		{
			string ntDir = txtNtFolder.Text.Trim();
			btnInstall.Enabled = false;
			btnUninstall.Enabled = false;

			await Task.Run(() =>
			{
				try
				{
					string customDir = Path.Combine(ntDir, "bin", "Custom");
					string indDir = Path.Combine(customDir, "Indicators");
					string addOnsDir = Path.Combine(customDir, "AddOns");
					string importDir = Path.Combine(ntDir, "import");
					string wsDir = Path.Combine(ntDir, "workspaces");

					Directory.CreateDirectory(customDir);
					Directory.CreateDirectory(indDir);
					Directory.CreateDirectory(addOnsDir);
					Directory.CreateDirectory(importDir);
					Directory.CreateDirectory(wsDir);

					CleanupLegacyWrappersAndDlls(customDir, indDir, addOnsDir);

					AppendLog("Installing AK10X_Logo.png...");
					ExtractResourceToFile("AK10X_Logo.png", Path.Combine(indDir, "AK10X_Logo.png"));

					string suiteDllTarget = Path.Combine(customDir, "AK10X.Suite.dll");
					AppendLog("Installing Protected Vendor Assembly: AK10X.Suite.dll...");
					try
					{
						ExtractResourceToFile("AK10X.Suite.dll", suiteDllTarget);
					}
					catch
					{
						string bak = suiteDllTarget + ".old_" + DateTime.Now.ToString("HHmmss");
						try { if (File.Exists(suiteDllTarget)) File.Move(suiteDllTarget, bak); } catch { }
						ExtractResourceToFile("AK10X.Suite.dll", suiteDllTarget);
					}

					AppendLog("Installing Zero-Dependency AddOn Bridge: AddOns\\AK10XTradingWindow.cs...");
					ExtractResourceToFile("AK10XTradingWindow_Wrapper.cs", Path.Combine(addOnsDir, "AK10XTradingWindow.cs"));

					try
					{
						ExtractResourceToFile("AK10X_Workspace.xml", Path.Combine(wsDir, "AK10X Trading Screen.xml"));
						AppendLog("Installed Workspace: AK10X Trading Screen.xml");
					}
					catch { }

					CleanConfigXmlReferences(Path.Combine(ntDir, "Config.xml"));
					UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), true);

					string zipTarget = Path.Combine(importDir, "AK10X_Protected_NT8_AddOn.zip");
					ExtractResourceToFile("AK10X_Protected_NT8_AddOn.zip", zipTarget);
					AppendLog("Saved Protected Add-On ZIP to: " + zipTarget);

					string csprojPath = Path.Combine(customDir, "NinjaTrader.Custom.csproj");
					if (File.Exists(csprojPath))
					{
						AppendLog("Compiling NinjaTrader.Custom.dll cleanly...");
						TryBuildCustomDll(customDir, csprojPath);
					}

					AppendLog("SUCCESS! All 6 AK10X modules installed in Protected Vendor Binary Mode!");
					MessageBox.Show(this,
						"AK10X Complete Suite has been installed in Protected Mode!\n\nIncluded Modules:\n• AK10XIndianOrderflow (Accelpix Default + Data Feed Option)\n• AK10XGlobalOrderflow (NinjaTrader Feed Default + Data Feed Option)\n• AK10XTradingWindow (AK10X Trading Screen)\n• AK10XIndiaMarketProfile\n• AK10XGlobalMarketProfile\n• AK10XMarketProfileScanner\n\nAll source code is protected inside AK10X.Suite.dll with zero NinjaScript Editor compile errors.",
						"AK10X Suite Installed",
						MessageBoxButtons.OK,
						MessageBoxIcon.Information);
				}
				catch (Exception ex)
				{
					AppendLog("ERROR: " + ex.Message);
					MessageBox.Show(this, "Installation error:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			});

			btnInstall.Enabled = true;
			btnUninstall.Enabled = true;
		}

		private void OnExportZipClick(object sender, EventArgs e)
		{
			try
			{
				string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				string outZip = Path.Combine(desktop, "AK10X_Protected_NT8_AddOn.zip");
				ExtractResourceToFile("AK10X_Protected_NT8_AddOn.zip", outZip);
				AppendLog("Exported Protected NinjaTrader 8 Add-On ZIP to Desktop: " + outZip);
				MessageBox.Show(this,
					"Saved Protected Add-On ZIP to Desktop:\n" + outZip + "\n\nContains all 6 AK10X modules in Protected Mode (AK10X.Suite.dll).",
					"Protected Add-On ZIP Saved",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				AppendLog("ERROR saving ZIP: " + ex.Message);
			}
		}

		private void OnUninstallClick(object sender, EventArgs e)
		{
			string ntDir = txtNtFolder.Text.Trim();
			if (MessageBox.Show(this, "Remove all 6 AK10X Suite modules from NinjaTrader 8?", "Confirm Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				return;

			try
			{
				string customDir = Path.Combine(ntDir, "bin", "Custom");
				string indDir = Path.Combine(customDir, "Indicators");
				string addOnsDir = Path.Combine(customDir, "AddOns");
				string suiteDll = Path.Combine(customDir, "AK10X.Suite.dll");
				string logoTarget = Path.Combine(indDir, "AK10X_Logo.png");
				string addOnBridge = Path.Combine(addOnsDir, "AK10XTradingWindow.cs");

				CleanupLegacyWrappersAndDlls(customDir, indDir, addOnsDir);

				if (File.Exists(suiteDll)) try { File.Delete(suiteDll); } catch { }
				if (File.Exists(logoTarget)) try { File.Delete(logoTarget); } catch { }
				if (File.Exists(addOnBridge)) try { File.Delete(addOnBridge); } catch { }

				CleanConfigXmlReferences(Path.Combine(ntDir, "Config.xml"));
				UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), false);

				AppendLog("Uninstalled AK10X Complete Suite cleanly.");
				MessageBox.Show(this, "AK10X Complete Suite has been uninstalled.", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				AppendLog("Uninstall error: " + ex.Message);
			}
		}

		private static void CleanConfigXmlReferences(string configXmlPath)
		{
			if (!File.Exists(configXmlPath))
				return;
			string xml = File.ReadAllText(configXmlPath);
			string updated = Regex.Replace(xml, @"^[ \t]*<string>\*MyDocuments\*\\NinjaTrader 8\\bin\\Custom\\AK10X\.[^<]+</string>\r?\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
			updated = updated.Replace("<Name>AK10XWindowStacker</Name>", "<Name>AK10XTradingWindow</Name>");
			if (!string.Equals(xml, updated, StringComparison.Ordinal))
			{
				File.WriteAllText(configXmlPath, updated, new UTF8Encoding(false));
			}
		}

		private static void UpdateCsproj(string csprojPath, bool install)
		{
			if (!File.Exists(csprojPath))
				return;
			string xml = File.ReadAllText(csprojPath);

			xml = Regex.Replace(xml, @"[ \t]*<Reference Include=""AK10X\.[^""]+"">[\s\S]*?</Reference>\r?\n?", "", RegexOptions.IgnoreCase);
			xml = Regex.Replace(xml, @"^[ \t]*<Compile Include=""Indicators\\(?:INDMarketProfile|MarketProfile|AK10X)[^""]*\.cs""[ \t]*/>\r?\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
			xml = Regex.Replace(xml, @"^[ \t]*<Compile Include=""AddOns\\AK10XWindowStacker\.cs""[ \t]*/>\r?\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

			string addOnCompile = "<Compile Include=\"AddOns\\AK10XTradingWindow.cs\" />";
			if (install)
			{
				if (!xml.Contains(addOnCompile))
				{
					int cIdx = xml.LastIndexOf("<Compile Include=", StringComparison.Ordinal);
					if (cIdx >= 0)
					{
						int lineEnd = xml.IndexOf("/>", cIdx, StringComparison.Ordinal);
						if (lineEnd > cIdx)
							xml = xml.Insert(lineEnd + 2, "\r\n    " + addOnCompile);
					}
				}
			}
			else
			{
				xml = xml.Replace("    " + addOnCompile + "\r\n", "").Replace(addOnCompile, "");
			}

			File.WriteAllText(csprojPath, xml, new UTF8Encoding(false));
		}

		private void TryBuildCustomDll(string customDir, string csprojPath)
		{
			try
			{
				string outDir = Path.Combine(Path.GetTempPath(), "ak10x_nt8_build_" + Guid.NewGuid().ToString("N").Substring(0, 6));
				Directory.CreateDirectory(outDir);

				ProcessStartInfo psi = new ProcessStartInfo("dotnet", string.Format("build \"{0}\" -c Release -o \"{1}\" /p:GenerateAssemblyInfo=false", csprojPath, outDir))
				{
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using (Process p = Process.Start(psi))
				{
					p.WaitForExit(60000);
					string builtDll = Path.Combine(outDir, "NinjaTrader.Custom.dll");
					if (p.ExitCode == 0 && File.Exists(builtDll))
					{
						string targetDll = Path.Combine(customDir, "NinjaTrader.Custom.dll");
						try
						{
							File.Copy(builtDll, targetDll, true);
						}
						catch
						{
							string bakDll = targetDll + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
							try { if (File.Exists(targetDll)) File.Move(targetDll, bakDll); } catch { }
							File.Copy(builtDll, targetDll, true);
						}
						AppendLog("Compiled and hot-swapped NinjaTrader.Custom.dll automatically (0 Errors)!");
					}
					else
					{
						AppendLog("Files installed. Press F5 in NinjaScript Editor inside NinjaTrader 8 to finalize compilation.");
					}
				}

				if (Directory.Exists(outDir)) try { Directory.Delete(outDir, true); } catch { }
			}
			catch
			{
				AppendLog("Files installed. Press F5 in NinjaScript Editor inside NinjaTrader 8 to compile.");
			}
		}

		[STAThread]
		public static void Main(string[] args)
		{
			if (args != null && args.Length > 0 && string.Equals(args[0], "--silent", StringComparison.OrdinalIgnoreCase))
			{
				string ntDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
				string customDir = Path.Combine(ntDir, "bin", "Custom");
				string indDir = Path.Combine(customDir, "Indicators");
				string addOnsDir = Path.Combine(customDir, "AddOns");
				Directory.CreateDirectory(indDir);
				Directory.CreateDirectory(addOnsDir);
				CleanupLegacyWrappersAndDlls(customDir, indDir, addOnsDir);
				ExtractResourceToFile("AK10X_Logo.png", Path.Combine(indDir, "AK10X_Logo.png"));
				string suiteDllTarget = Path.Combine(customDir, "AK10X.Suite.dll");
				try
				{
					ExtractResourceToFile("AK10X.Suite.dll", suiteDllTarget);
				}
				catch
				{
					string bak = suiteDllTarget + ".old_" + DateTime.Now.ToString("HHmmss");
					try { if (File.Exists(suiteDllTarget)) File.Move(suiteDllTarget, bak); } catch { }
					ExtractResourceToFile("AK10X.Suite.dll", suiteDllTarget);
				}
				ExtractResourceToFile("AK10XTradingWindow_Wrapper.cs", Path.Combine(addOnsDir, "AK10XTradingWindow.cs"));
				UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), true);
				return;
			}

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new SetupForm());
		}
	}
}
