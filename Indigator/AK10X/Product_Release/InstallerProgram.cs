using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("AK10X Complete Suite Setup (Protected Mode)")]
[assembly: AssemblyProduct("AK10X Orderflow, Market Profile, Scanner & Trading Screen for NinjaTrader 8")]
[assembly: AssemblyCompany("AK10X Trading Systems")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace AK10XInstaller
{
	public sealed class SetupForm : Form
	{
		private TextBox txtNtFolder;
		private TextBox txtLog;
		private Button btnInstall;
		private Button btnExportZip;
		private Button btnUninstall;

		private static readonly string[] WrapperFiles = new string[]
		{
			"Indicators\\AK10XOrderflow.cs",
			"Indicators\\INDMarketProfile50Charts.cs",
			"Indicators\\MarketProfile50Charts.cs",
			"Indicators\\MarketProfileScanner50Charts.cs",
			"AddOns\\AK10XWindowStacker.cs"
		};

		public SetupForm()
		{
			Text = "AK10X Complete Suite — NinjaTrader 8 Protected Setup";
			Size = new Size(760, 610);
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
				Text = "AK10XOrderflow • AK10XChartStackerWindow • INDMarketProfile50Charts • MarketProfile50Charts • Scanner",
				Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
				ForeColor = Color.FromArgb(185, 198, 215),
				Location = new Point(106, 46),
				AutoSize = true
			};
			headerPanel.Controls.Add(lblSub);

			Label lblVer = new Label
			{
				Text = "Protected Binary Mode (Source Code Hidden inside AK10X.Suite.dll) • All 5 Modules in One EXE",
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
				Location = new Point(20, 114),
				AutoSize = true,
				Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
			};
			Controls.Add(lblFolder);

			string defaultNtDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
			txtNtFolder = new TextBox
			{
				Text = defaultNtDir,
				Location = new Point(20, 138),
				Size = new Size(595, 26),
				BackColor = Color.FromArgb(22, 28, 40),
				ForeColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle
			};
			Controls.Add(txtNtFolder);

			Button btnBrowse = new Button
			{
				Text = "Browse...",
				Location = new Point(625, 136),
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
				Location = new Point(20, 174),
				Size = new Size(702, 118),
				ForeColor = Color.FromArgb(255, 200, 80)
			};

			string[] modDescriptions = new string[]
			{
				"✔  1. AK10XOrderflow — 5-Min Orderflow Footprint, 1:2 Logic, Stacked Imbalances & 7-Row Statistic Grid",
				"✔  2. AK10XChartStackerWindow — AK10X Trading Screen (Control Center -> New -> AK10X Trading Screen)",
				"✔  3. INDMarketProfile50Charts — Indian NSE Market Profile (Daily/Weekly/Monthly, Split/Merge, Virgin Levels)",
				"✔  4. MarketProfile50Charts — 3-Session Forex/Commodity Market Profile (Asia / London / New York)",
				"✔  5. MarketProfileScanner50Charts — Real-Time Accelpix Live Market Profile Scanner (Indicator + Window)"
			};

			for (int i = 0; i < modDescriptions.Length; i++)
			{
				Label lblMod = new Label
				{
					Text = modDescriptions[i],
					Location = new Point(16, 22 + i * 18),
					AutoSize = true,
					Font = new Font("Segoe UI", 8.8f, FontStyle.Regular),
					ForeColor = Color.FromArgb(220, 230, 242)
				};
				grpModules.Controls.Add(lblMod);
			}
			Controls.Add(grpModules);

			btnInstall = new Button
			{
				Text = "Install All 5 Modules (Protected)",
				Location = new Point(20, 304),
				Size = new Size(276, 42),
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
				Location = new Point(308, 304),
				Size = new Size(256, 42),
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
				Location = new Point(576, 304),
				Size = new Size(146, 42),
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
				Location = new Point(20, 356),
				AutoSize = true,
				Font = new Font("Segoe UI", 9f, FontStyle.Bold)
			};
			Controls.Add(lblLog);

			txtLog = new TextBox
			{
				Location = new Point(20, 378),
				Size = new Size(702, 178),
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
			AppendLog("Mode: PROTECTED BINARY (C# source code is hidden inside compiled AK10X.Suite.dll).");
			AppendLog("Click [Install All 5 Modules (Protected)] to deploy the complete AK10X suite.");
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

					// Remove any old plain-text root duplicates or legacy single-indicator DLL
					string[] legacyFiles = new string[]
					{
						Path.Combine(customDir, "AK10XWindowStacker.cs"),
						Path.Combine(customDir, "INDMarketProfile50Charts.cs"),
						Path.Combine(customDir, "AK10X.MarketProfile50Charts.dll")
					};
					foreach (string lf in legacyFiles)
					{
						if (File.Exists(lf))
						{
							try { File.Delete(lf); } catch { }
						}
					}

					AppendLog("Installing AK10X_Logo.png...");
					ExtractResourceToFile("AK10X_Logo.png", Path.Combine(indDir, "AK10X_Logo.png"));

					string suiteDllTarget = Path.Combine(customDir, "AK10X.Suite.dll");
					AppendLog("Installing Protected Suite Binary: AK10X.Suite.dll...");
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

					AppendLog("Installing Protected Thin Wrappers (Source Code Hidden):");
					AppendLog("  -> Indicators\\AK10XOrderflow.cs");
					ExtractResourceToFile("AK10XOrderflow_Wrapper.cs", Path.Combine(indDir, "AK10XOrderflow.cs"));

					AppendLog("  -> Indicators\\INDMarketProfile50Charts.cs");
					ExtractResourceToFile("INDMarketProfile50Charts_Wrapper.cs", Path.Combine(indDir, "INDMarketProfile50Charts.cs"));

					AppendLog("  -> Indicators\\MarketProfile50Charts.cs");
					ExtractResourceToFile("MarketProfile50Charts_Wrapper.cs", Path.Combine(indDir, "MarketProfile50Charts.cs"));

					AppendLog("  -> Indicators\\MarketProfileScanner50Charts.cs");
					ExtractResourceToFile("MarketProfileScanner50Charts_Wrapper.cs", Path.Combine(indDir, "MarketProfileScanner50Charts.cs"));

					AppendLog("  -> AddOns\\AK10XWindowStacker.cs");
					ExtractResourceToFile("AK10XWindowStacker_Wrapper.cs", Path.Combine(addOnsDir, "AK10XWindowStacker.cs"));

					try
					{
						ExtractResourceToFile("AK10X_Workspace.xml", Path.Combine(wsDir, "AK10X - 2-Screen Stacker.xml"));
						AppendLog("Installed Workspace: AK10X - 2-Screen Stacker.xml");
					}
					catch { }

					RegisterDllInConfigXml(Path.Combine(ntDir, "Config.xml"), true);
					UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), suiteDllTarget, true);

					string zipTarget = Path.Combine(importDir, "AK10X_Protected_NT8_AddOn.zip");
					ExtractResourceToFile("AK10X_Protected_NT8_AddOn.zip", zipTarget);
					AppendLog("Saved Protected Add-On ZIP to: " + zipTarget);

					string csprojPath = Path.Combine(customDir, "NinjaTrader.Custom.csproj");
					if (File.Exists(csprojPath))
					{
						AppendLog("Compiling NinjaTrader.Custom.dll with AK10X.Suite.dll...");
						TryBuildCustomDll(customDir, csprojPath);
					}

					AppendLog("SUCCESS! All 5 AK10X modules installed in Protected Mode (Source Code Hidden)!");
					MessageBox.Show(this,
						"AK10X Complete Suite has been installed in Protected Mode!\n\nIncluded Modules:\n• AK10XOrderflow\n• AK10XChartStackerWindow (AK10X Trading Screen)\n• INDMarketProfile50Charts\n• MarketProfile50Charts\n• MarketProfileScanner50Charts\n\nAll source code is protected inside AK10X.Suite.dll.",
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
					"Saved Protected Add-On ZIP to Desktop:\n" + outZip + "\n\nContains all 5 modules in Protected Mode (AK10X.Suite.dll).",
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
			if (MessageBox.Show(this, "Remove all 5 AK10X Suite modules from NinjaTrader 8?", "Confirm Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				return;

			try
			{
				string customDir = Path.Combine(ntDir, "bin", "Custom");
				string indDir = Path.Combine(customDir, "Indicators");
				string suiteDll = Path.Combine(customDir, "AK10X.Suite.dll");
				string oldDll = Path.Combine(customDir, "AK10X.MarketProfile50Charts.dll");
				string logoTarget = Path.Combine(indDir, "AK10X_Logo.png");

				if (File.Exists(suiteDll)) try { File.Delete(suiteDll); } catch { }
				if (File.Exists(oldDll)) try { File.Delete(oldDll); } catch { }
				if (File.Exists(logoTarget)) try { File.Delete(logoTarget); } catch { }

				foreach (string rel in WrapperFiles)
				{
					string fp = Path.Combine(customDir, rel);
					if (File.Exists(fp)) try { File.Delete(fp); } catch { }
				}

				RegisterDllInConfigXml(Path.Combine(ntDir, "Config.xml"), false);
				UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), suiteDll, false);

				AppendLog("Uninstalled AK10X Complete Suite cleanly.");
				MessageBox.Show(this, "AK10X Complete Suite has been uninstalled.", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				AppendLog("Uninstall error: " + ex.Message);
			}
		}

		private void RegisterDllInConfigXml(string configXmlPath, bool addReference)
		{
			if (!File.Exists(configXmlPath))
				return;
			string xml = File.ReadAllText(configXmlPath);
			string oldRef = "      <string>*MyDocuments*\\NinjaTrader 8\\bin\\Custom\\AK10X.MarketProfile50Charts.dll</string>";
			string newRef = "      <string>*MyDocuments*\\NinjaTrader 8\\bin\\Custom\\AK10X.Suite.dll</string>";

			xml = xml.Replace(oldRef + "\r\n", "").Replace(oldRef + "\n", "").Replace(oldRef, "");

			if (addReference)
			{
				if (!xml.Contains("AK10X.Suite.dll") && xml.Contains("</ArrayOfString>"))
				{
					xml = xml.Replace("    </ArrayOfString>", newRef + Environment.NewLine + "    </ArrayOfString>");
				}
			}
			else
			{
				xml = xml.Replace(newRef + "\r\n", "").Replace(newRef + "\n", "").Replace(newRef, "");
			}
			File.WriteAllText(configXmlPath, xml, Encoding.UTF8);
		}

		private void UpdateCsproj(string csprojPath, string dllFullPath, bool install)
		{
			if (!File.Exists(csprojPath))
				return;
			string xml = File.ReadAllText(csprojPath);

			string[] oldRefNames = new string[] { "AK10X.MarketProfile50Charts", "AK10X.Suite" };
			foreach (string rn in oldRefNames)
			{
				int rStart = xml.IndexOf("<Reference Include=\"" + rn + "\">", StringComparison.Ordinal);
				if (rStart >= 0)
				{
					int rEnd = xml.IndexOf("</Reference>", rStart, StringComparison.Ordinal);
					if (rEnd > rStart)
						xml = xml.Remove(rStart, (rEnd + "</Reference>".Length) - rStart);
				}
			}

			if (install)
			{
				string refBlock = string.Format("    <Reference Include=\"AK10X.Suite\">\r\n      <HintPath>{0}</HintPath>\r\n    </Reference>", dllFullPath);
				string anchor = "<Reference Include=\"NinjaTrader.Vendor\">";
				int idx = xml.IndexOf(anchor, StringComparison.Ordinal);
				if (idx >= 0)
					xml = xml.Insert(idx, refBlock + "\r\n    ");
			}

			foreach (string rel in WrapperFiles)
			{
				string compileLine = string.Format("<Compile Include=\"{0}\" />", rel);
				if (install)
				{
					if (!xml.Contains(compileLine))
					{
						int cIdx = xml.LastIndexOf("<Compile Include=", StringComparison.Ordinal);
						if (cIdx >= 0)
						{
							int lineEnd = xml.IndexOf("/>", cIdx, StringComparison.Ordinal);
							if (lineEnd > cIdx)
								xml = xml.Insert(lineEnd + 2, "\r\n    " + compileLine);
						}
					}
				}
				else
				{
					xml = xml.Replace("    " + compileLine + "\r\n", "").Replace(compileLine, "");
				}
			}

			File.WriteAllText(csprojPath, xml, Encoding.UTF8);
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
						AppendLog("Compiled and hot-swapped NinjaTrader.Custom.dll automatically!");
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
				ExtractResourceToFile("AK10X.Suite.dll", Path.Combine(customDir, "AK10X.Suite.dll"));
				ExtractResourceToFile("AK10XOrderflow_Wrapper.cs", Path.Combine(indDir, "AK10XOrderflow.cs"));
				ExtractResourceToFile("INDMarketProfile50Charts_Wrapper.cs", Path.Combine(indDir, "INDMarketProfile50Charts.cs"));
				ExtractResourceToFile("MarketProfile50Charts_Wrapper.cs", Path.Combine(indDir, "MarketProfile50Charts.cs"));
				ExtractResourceToFile("MarketProfileScanner50Charts_Wrapper.cs", Path.Combine(indDir, "MarketProfileScanner50Charts.cs"));
				ExtractResourceToFile("AK10XWindowStacker_Wrapper.cs", Path.Combine(addOnsDir, "AK10XWindowStacker.cs"));
				return;
			}

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new SetupForm());
		}
	}
}
