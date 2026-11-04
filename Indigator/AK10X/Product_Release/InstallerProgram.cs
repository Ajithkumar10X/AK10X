using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("AK10X Market Profile 50Charts Setup")]
[assembly: AssemblyProduct("AK10X Market Profile 50Charts for NinjaTrader 8")]
[assembly: AssemblyCompany("AK10X Trading")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace AK10XInstaller
{
	public sealed class SetupForm : Form
	{
		private TextBox txtNtFolder;
		private RadioButton rbProtected;
		private RadioButton rbSource;
		private TextBox txtLog;
		private Button btnInstall;
		private Button btnExportZip;
		private Button btnUninstall;

		public SetupForm()
		{
			Text = "AK10X Market Profile 50Charts — NinjaTrader 8 Setup Wizard";
			Size = new Size(720, 560);
			StartPosition = FormStartPosition.CenterScreen;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			BackColor = Color.FromArgb(14, 17, 24);
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
				Height = 96,
				BackColor = Color.FromArgb(20, 25, 35)
			};

			PictureBox picLogo = new PictureBox
			{
				Location = new Point(18, 12),
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
				Text = "AK10X Market Profile 50Charts (NinjaTrader 8)",
				Font = new Font("Segoe UI", 15f, FontStyle.Bold),
				ForeColor = Color.FromArgb(0, 220, 255),
				Location = new Point(104, 16),
				AutoSize = true
			};
			headerPanel.Controls.Add(lblTitle);

			Label lblSub = new Label
			{
				Text = "3-Session Forex Market Profile (Asia / London / New York) • Split & Merge • Virgin Levels",
				Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
				ForeColor = Color.FromArgb(180, 190, 205),
				Location = new Point(106, 46),
				AutoSize = true
			};
			headerPanel.Controls.Add(lblSub);

			Label lblVer = new Label
			{
				Text = "Version 1.0 • Ready for 1-Click Installation",
				Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
				ForeColor = Color.FromArgb(120, 220, 140),
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
				Size = new Size(560, 26),
				BackColor = Color.FromArgb(24, 30, 42),
				ForeColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle
			};
			Controls.Add(txtNtFolder);

			Button btnBrowse = new Button
			{
				Text = "Browse...",
				Location = new Point(590, 134),
				Size = new Size(92, 28),
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

			GroupBox grpMode = new GroupBox
			{
				Text = "Installation Mode",
				Location = new Point(20, 174),
				Size = new Size(662, 82),
				ForeColor = Color.FromArgb(255, 200, 80)
			};

			rbProtected = new RadioButton
			{
				Text = "Protected Binary DLL Mode (Recommended for Sharing — Hides C# Source Code inside compiled DLL)",
				Location = new Point(16, 24),
				Size = new Size(630, 22),
				Checked = true,
				ForeColor = Color.FromArgb(230, 235, 245)
			};
			grpMode.Controls.Add(rbProtected);

			rbSource = new RadioButton
			{
				Text = "Full Source Code Mode (Installs full editable MarketProfile50Charts.cs into Indicators folder)",
				Location = new Point(16, 50),
				Size = new Size(630, 22),
				ForeColor = Color.FromArgb(190, 200, 215)
			};
			grpMode.Controls.Add(rbSource);
			Controls.Add(grpMode);

			btnInstall = new Button
			{
				Text = "Install to NinjaTrader 8",
				Location = new Point(20, 270),
				Size = new Size(250, 40),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(22, 135, 75),
				ForeColor = Color.White,
				Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			btnInstall.Click += OnInstallClick;
			Controls.Add(btnInstall);

			btnExportZip = new Button
			{
				Text = "Save Add-On .ZIP to Desktop",
				Location = new Point(282, 270),
				Size = new Size(240, 40),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(25, 95, 145),
				ForeColor = Color.White,
				Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			btnExportZip.Click += OnExportZipClick;
			Controls.Add(btnExportZip);

			btnUninstall = new Button
			{
				Text = "Uninstall",
				Location = new Point(534, 270),
				Size = new Size(148, 40),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.FromArgb(130, 40, 40),
				ForeColor = Color.White,
				Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
				Cursor = Cursors.Hand
			};
			btnUninstall.Click += OnUninstallClick;
			Controls.Add(btnUninstall);

			Label lblLog = new Label
			{
				Text = "Installation Status Log:",
				Location = new Point(20, 322),
				AutoSize = true,
				Font = new Font("Segoe UI", 9f, FontStyle.Bold)
			};
			Controls.Add(lblLog);

			txtLog = new TextBox
			{
				Location = new Point(20, 344),
				Size = new Size(662, 160),
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BackColor = Color.FromArgb(10, 12, 18),
				ForeColor = Color.FromArgb(140, 235, 165),
				Font = new Font("Consolas", 9f),
				BorderStyle = BorderStyle.FixedSingle
			};
			Controls.Add(txtLog);

			AppendLog("Ready. Detected NinjaTrader 8 folder: " + defaultNtDir);
			AppendLog("Click [Install to NinjaTrader 8] to install AK10X Market Profile 50Charts.");
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
			bool protectedMode = rbProtected.Checked;
			btnInstall.Enabled = false;
			btnUninstall.Enabled = false;

			await Task.Run(() =>
			{
				try
				{
					string customDir = Path.Combine(ntDir, "bin", "Custom");
					string indDir = Path.Combine(customDir, "Indicators");
					string importDir = Path.Combine(ntDir, "import");
					Directory.CreateDirectory(customDir);
					Directory.CreateDirectory(indDir);
					Directory.CreateDirectory(importDir);

					AppendLog("Installing AK10X_Logo.png...");
					ExtractResourceToFile("AK10X_Logo.png", Path.Combine(indDir, "AK10X_Logo.png"));

					string dllTarget = Path.Combine(customDir, "AK10X.MarketProfile50Charts.dll");
					string csTarget = Path.Combine(indDir, "MarketProfile50Charts.cs");

					if (protectedMode)
					{
						AppendLog("Installing Protected Binary: AK10X.MarketProfile50Charts.dll...");
						ExtractResourceToFile("AK10X.MarketProfile50Charts.dll", dllTarget);
						AppendLog("Installing thin NinjaScript wrapper: MarketProfile50Charts.cs...");
						ExtractResourceToFile("MarketProfile50Charts_Wrapper.cs", csTarget);
						RegisterDllInConfigXml(Path.Combine(ntDir, "Config.xml"), true);
						UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), dllTarget, true, true);
					}
					else
					{
						if (File.Exists(dllTarget))
						{
							try { File.Delete(dllTarget); } catch { }
						}
						AppendLog("Installing Full Source: MarketProfile50Charts.cs...");
						ExtractResourceToFile("MarketProfile50Charts_Full.cs", csTarget);
						RegisterDllInConfigXml(Path.Combine(ntDir, "Config.xml"), false);
						UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), dllTarget, false, true);
					}

					// Also place the ready-to-import Add-On ZIP in Documents\NinjaTrader 8\import\
					string zipTarget = Path.Combine(importDir, "AK10X_MarketProfile50Charts_Protected_NT8_AddOn.zip");
					ExtractResourceToFile("AK10X_MarketProfile50Charts_Protected_NT8_AddOn.zip", zipTarget);
					AppendLog("Saved backup Add-On package to: " + zipTarget);

					// Attempt automatic compilation of NinjaTrader.Custom.csproj if dotnet SDK is available
					string csprojPath = Path.Combine(customDir, "NinjaTrader.Custom.csproj");
					if (File.Exists(csprojPath))
					{
						AppendLog("Compiling NinjaTrader.Custom.dll...");
						TryBuildCustomDll(customDir, csprojPath);
					}

					AppendLog("SUCCESS! AK10X Market Profile 50Charts is installed!");
					AppendLog("Open NinjaTrader 8 -> Right-click Chart -> Indicators -> MarketProfile50Charts.");
					MessageBox.Show(this,
						"AK10X Market Profile 50Charts has been installed successfully!\n\nOpen NinjaTrader 8 (or press F5 on your chart) and select 'MarketProfile50Charts' under Indicators.",
						"Installation Complete",
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
				string outZip = Path.Combine(desktop, "AK10X_MarketProfile50Charts_Protected_NT8_AddOn.zip");
				ExtractResourceToFile("AK10X_MarketProfile50Charts_Protected_NT8_AddOn.zip", outZip);
				AppendLog("Exported NinjaTrader 8 Add-On ZIP to Desktop: " + outZip);
				MessageBox.Show(this,
					"Saved Add-On ZIP to Desktop:\n" + outZip + "\n\nAnyone can import this in NinjaTrader 8 via:\nControl Center -> Tools -> Import -> NinjaScript Add-On...",
					"Add-On ZIP Saved",
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
			if (MessageBox.Show(this, "Remove AK10X Market Profile 50Charts from NinjaTrader 8?", "Confirm Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				return;

			try
			{
				string customDir = Path.Combine(ntDir, "bin", "Custom");
				string indDir = Path.Combine(customDir, "Indicators");
				string dllTarget = Path.Combine(customDir, "AK10X.MarketProfile50Charts.dll");
				string csTarget = Path.Combine(indDir, "MarketProfile50Charts.cs");
				string logoTarget = Path.Combine(indDir, "AK10X_Logo.png");

				if (File.Exists(dllTarget)) File.Delete(dllTarget);
				if (File.Exists(csTarget)) File.Delete(csTarget);
				if (File.Exists(logoTarget)) File.Delete(logoTarget);

				RegisterDllInConfigXml(Path.Combine(ntDir, "Config.xml"), false);
				UpdateCsproj(Path.Combine(customDir, "NinjaTrader.Custom.csproj"), dllTarget, false, false);

				AppendLog("Uninstalled AK10X Market Profile 50Charts cleanly.");
				MessageBox.Show(this, "AK10X Market Profile 50Charts has been uninstalled.", "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
			string refLine = "      <string>*MyDocuments*\\NinjaTrader 8\\bin\\Custom\\AK10X.MarketProfile50Charts.dll</string>";
			if (addReference)
			{
				if (!xml.Contains("AK10X.MarketProfile50Charts.dll") && xml.Contains("</ArrayOfString>"))
				{
					xml = xml.Replace("    </ArrayOfString>", refLine + Environment.NewLine + "    </ArrayOfString>");
					File.WriteAllText(configXmlPath, xml, Encoding.UTF8);
					AppendLog("Registered AK10X.MarketProfile50Charts.dll in Config.xml.");
				}
			}
			else
			{
				if (xml.Contains("AK10X.MarketProfile50Charts.dll"))
				{
					xml = xml.Replace(refLine + "\r\n", "").Replace(refLine + "\n", "").Replace(refLine, "");
					File.WriteAllText(configXmlPath, xml, Encoding.UTF8);
				}
			}
		}

		private void UpdateCsproj(string csprojPath, string dllFullPath, bool includeDllRef, bool includeCsCompile)
		{
			if (!File.Exists(csprojPath))
				return;
			string xml = File.ReadAllText(csprojPath);
			string refBlock = string.Format("    <Reference Include=\"AK10X.MarketProfile50Charts\">\r\n      <HintPath>{0}</HintPath>\r\n    </Reference>", dllFullPath);

			// Clean existing AK10X reference if any
			int rStart = xml.IndexOf("<Reference Include=\"AK10X.MarketProfile50Charts\">", StringComparison.Ordinal);
			if (rStart >= 0)
			{
				int rEnd = xml.IndexOf("</Reference>", rStart, StringComparison.Ordinal);
				if (rEnd > rStart)
					xml = xml.Remove(rStart, (rEnd + "</Reference>".Length) - rStart);
			}

			if (includeDllRef)
			{
				string anchor = "<Reference Include=\"NinjaTrader.Vendor\">";
				int idx = xml.IndexOf(anchor, StringComparison.Ordinal);
				if (idx >= 0)
					xml = xml.Insert(idx, refBlock + "\r\n    ");
			}

			string compileLine = "<Compile Include=\"Indicators\\MarketProfile50Charts.cs\" />";
			if (includeCsCompile)
			{
				if (!xml.Contains("Indicators\\MarketProfile50Charts.cs"))
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
					p.WaitForExit(45000);
					string builtDll = Path.Combine(outDir, "NinjaTrader.Custom.dll");
					if (p.ExitCode == 0 && File.Exists(builtDll))
					{
						string targetDll = Path.Combine(customDir, "NinjaTrader.Custom.dll");
						string bakDll = targetDll + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
						try { if (File.Exists(targetDll)) File.Move(targetDll, bakDll); } catch { }
						File.Copy(builtDll, targetDll, true);
						try { if (File.Exists(bakDll)) File.Delete(bakDll); } catch { }
						AppendLog("Compiled and hot-swapped NinjaTrader.Custom.dll automatically!");
					}
					else
					{
						AppendLog("Note: Open NinjaScript Editor in NinjaTrader 8 and press F5 to finalize compilation.");
					}
				}

				string objDir = Path.Combine(customDir, "obj");
				string binDir = Path.Combine(customDir, "bin");
				if (Directory.Exists(objDir)) try { Directory.Delete(objDir, true); } catch { }
				if (Directory.Exists(binDir)) try { Directory.Delete(binDir, true); } catch { }
				if (Directory.Exists(outDir)) try { Directory.Delete(outDir, true); } catch { }
			}
			catch
			{
				AppendLog("Files installed. Press F5 in NinjaScript Editor inside NinjaTrader 8 to compile.");
			}
		}

		[STAThread]
		public static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new SetupForm());
		}
	}
}