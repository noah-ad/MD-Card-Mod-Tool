using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class StandaloneResourceTests
{
	internal static void Run(string root, string output)
	{
		root = StandaloneResourceService.ResolveRoot(root) ?? throw new Exception("0000 root not recognized");
		Directory.CreateDirectory(output);
		string[] array = StandaloneResourceService.EnumerateBundles(root).Take(60).ToArray();
		Dictionary<string, string> source = array.ToDictionary((string f) => f, Hash);
		List<TexRef> list = array.SelectMany<string, TexRef>(delegate(string f)
		{
			try
			{
				return StandaloneResourceService.ReadBundle(f, root);
			}
			catch
			{
				return Array.Empty<TexRef>();
			}
		}).ToList();
		if (list.Count == 0)
		{
			throw new Exception("No mobile textures found");
		}
		int num = 0;
		string text = Path.Combine(output, "write-" + Guid.NewGuid().ToString("N"), "0000");
		foreach (TexRef texture in list.Take(10))
		{
			byte[] array2 = StandaloneResourceService.Decode(texture);
			using MemoryStream stream = new MemoryStream(array2);
			using System.Drawing.Image image = System.Drawing.Image.FromStream(stream);
			if (image.Width != texture.Width || image.Height != texture.Height)
			{
				throw new Exception("Dimensions changed");
			}
			File.WriteAllBytes(Path.Combine(output, texture.Name + ".png"), array2);
			num++;
			string text2 = Path.Combine(text, texture.RelativeBundlePath);
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			File.Copy(texture.BundlePath, text2, overwrite: true);
			TexRef texRef = StandaloneResourceService.ReadBundle(text2, text).Single((TexRef t) => t.PathId == texture.PathId && t.AssetFileName == texture.AssetFileName);
			string text3 = Hash(text2);
			using Image<Rgba32> image2 = new Image<Rgba32>(texRef.Width, texRef.Height);
			image2[0, 0] = new Rgba32(31, 93, 201, 0);
			image2[texRef.Width - 1, texRef.Height - 1] = new Rgba32(199, 77, 12, 128);
			using MemoryStream memoryStream = new MemoryStream();
			image2.Save(memoryStream, new PngEncoder
			{
				TransparentColorMode = PngTransparentColorMode.Preserve
			});
			StandaloneModService.Replace(text, texRef, memoryStream.ToArray());
			if (Hash(text2) == text3 || !StandaloneModService.HasBackup(text, texRef))
			{
				throw new Exception("Mobile replacement or backup missing");
			}
			string text4 = Hash(text2);
			try
			{
				StandaloneModService.Replace(text, texRef, array2, delegate
				{
					throw new IOException("injected failure");
				});
				throw new Exception("Failure not raised");
			}
			catch (IOException ex) when (ex.Message == "injected failure")
			{
			}
			if (Hash(text2) != text4)
			{
				throw new Exception("Failure changed target");
			}
			StandaloneModService.Replace(text, texRef, array2);
			StandaloneModService.Restore(text, texRef);
			if (Hash(text2) != text3)
			{
				throw new Exception("First backup not restored exactly");
			}
		}
		using StandaloneResourceForm standaloneResourceForm = new StandaloneResourceForm(root)
		{
			AutoScan = false,
			ShowInTaskbar = false
		};
		standaloneResourceForm.Show();
		Application.DoEvents();
		((List<TexRef>)typeof(StandaloneResourceForm).GetField("all", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm)).AddRange(list);
		TextBox obj = (TextBox)typeof(StandaloneResourceForm).GetField("search", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm);
		ListView listView = (ListView)typeof(StandaloneResourceForm).GetField("list", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm);
		PictureBox pictureBox = (PictureBox)typeof(StandaloneResourceForm).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm);
		obj.Text = list[0].Name;
		typeof(StandaloneResourceForm).GetMethod("ApplyFilter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(standaloneResourceForm, new object[1] { false });
		if (listView.VirtualListSize == 0)
		{
			throw new Exception("Search failed");
		}
		listView.SelectedIndices.Add(0);
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (pictureBox.Image == null && stopwatch.Elapsed.TotalSeconds < 30.0)
		{
			Application.DoEvents();
			Thread.Sleep(15);
		}
		if (pictureBox.Image == null || pictureBox.SizeMode != PictureBoxSizeMode.Zoom)
		{
			throw new Exception("Preview missing");
		}
		float[] array3 = new float[2] { 1f, 1.5f };
		foreach (float num3 in array3)
		{
			if (num3 != 1f)
			{
				standaloneResourceForm.Scale(new System.Drawing.SizeF(num3, num3));
			}
			standaloneResourceForm.PerformLayout();
			Application.DoEvents();
			Button button = (Button)typeof(StandaloneResourceForm).GetField("export", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm);
			if (!button.Visible || !button.Parent.ClientRectangle.Contains(button.Bounds))
			{
				throw new Exception("Export button clipped");
			}
			using Bitmap bitmap = new Bitmap(standaloneResourceForm.Width, standaloneResourceForm.Height);
			standaloneResourceForm.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, standaloneResourceForm.Size));
			bitmap.Save(Path.Combine(output, $"viewer-{num3}.png"));
		}
		standaloneResourceForm.Close();
		using (StandaloneResourceForm standaloneResourceForm2 = new StandaloneResourceForm(root)
		{
			ShowInTaskbar = false
		})
		{
			standaloneResourceForm2.Show();
			Application.DoEvents();
			Button button2 = (Button)typeof(StandaloneResourceForm).GetField("stop", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm2);
			Label label = (Label)typeof(StandaloneResourceForm).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm2);
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			while (stopwatch2.Elapsed.TotalSeconds < 4.0)
			{
				Application.DoEvents();
				Thread.Sleep(15);
			}
			button2.PerformClick();
			Task task = (Task)typeof(StandaloneResourceForm).GetField("scanTask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(standaloneResourceForm2);
			stopwatch2.Restart();
			while (task != null && !task.IsCompleted && stopwatch2.Elapsed.TotalSeconds < 30.0)
			{
				Application.DoEvents();
				Thread.Sleep(15);
			}
			if (task == null || !task.IsCompleted)
			{
				throw new Exception("Scan did not cancel");
			}
			Console.WriteLine("scanCancel=" + label.Text);
			standaloneResourceForm2.Close();
		}
		using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();
		try
		{
			StandaloneResourceService.Scan(root, delegate
			{
			}, cancellationTokenSource.Token);
			throw new Exception("Cancellation ignored");
		}
		catch (OperationCanceledException)
		{
		}
		if (source.Any((KeyValuePair<string, string> h) => Hash(h.Key) != h.Value))
		{
			throw new Exception("Source files changed");
		}
		Console.WriteLine($"sampleBundles={array.Length}; textures={list.Count}; decoded={num}; mobileWriteRestore={num}; alphaRgbRoundTrip=True; failurePreservesTarget=True; search=True; uiPreview=True; cancel=True; sourceWrites=False; ready=True");
	}

	private static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}
}
