using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using SharpSvn;

namespace CompareSvnRepositories
{
	class Program
	{
		static readonly TextWriter Tw = new StreamWriter("bad-blames", false) { AutoFlush = true };

		static string _leftRepo;
		static int _leftRepoRevision;

		static string _rightRepo;
		static int _rightRepoRevision;

		readonly static SvnClient SvnClient = new SvnClient();

		static void Main(string[] args)
		{
			var branch = "";
			List<string> paths = null;

			for (var i = 0; i < args.Length; i++)
			{
				if(args[i] == "--left-repo")
				{
					_leftRepo = args[++i].TrimEnd('/', '\\') + "/";
					continue;
				}

				if (args[i] == "--right-repo")
				{
					_rightRepo = args[++i].TrimEnd('/', '\\') + "/";
					continue;
				}

				if (args[i] == "--rev")
				{
					_rightRepoRevision = Int32.Parse(args[++i]);
					_leftRepoRevision = _rightRepoRevision;
					continue;
				}

				if (args[i] == "--left-rev")
				{
					_leftRepoRevision = Int32.Parse(args[++i]);
					continue;
				}

				if (args[i] == "--right-rev")
				{
					_rightRepoRevision = Int32.Parse(args[++i]);
					continue;
				}

				if (args[i] == "--branch")
				{
					branch = args[++i];
					continue;
				}

				if (args[i] == "--paths")
				{
					paths = File.ReadLines(args[++i]).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
					continue;
				}
			}

			if (Directory.Exists("_compare"))
				Directory.Delete("_compare", true);

			if(paths == null)
			{
				var files1 = GetBranchFiles(_rightRepo, branch, _rightRepoRevision);
				var files2 = GetBranchFiles(_leftRepo, branch, _leftRepoRevision);

				if (files1.Count != files2.Count)
					throw new Exception(string.Format("Count of files inconsistent {0} != {1}", files1.Count, files2.Count));

				for (var i = 0; i < files1.Count; i++)
				{
					if (files1[i] != files2[i])
						throw new Exception(string.Format("Not equal files: {0} != {1}", files1[i], files2[i]));
				}

				paths = files1;
			}

			for (var i = 0; i < paths.Count; i++)
			{
				var path = paths[i];

				Console.WriteLine("{0} / {1}: {2}", i, paths.Count, path);

				var fullRel = branch + "/" + path;

				if (!CompareBlames(fullRel))
				{
					SaveBlames(fullRel);
					Tw.WriteLine("{0}", path);
				}
			}
		}

		static List<string> GetBranchFiles(string repo, string branch, int revision)
		{
			// read files list
			var args = new SvnListArgs {
				Depth = SvnDepth.Infinity
			};

			Collection<SvnListEventArgs> list;
			SvnClient.GetList(new SvnUriTarget(repo + branch, new SvnRevision(revision)), args, out list);

			var files = list
				.Where(li => li.Entry.NodeKind == SvnNodeKind.File)
				.Select(li => li.Path)
				.OrderBy(p => p)
				.ToList()
			;

			return files;
		}

		static bool CompareBlames(string relUrl)
		{
			try
			{
				var newBlames = GetBlame(_rightRepo + relUrl, _rightRepoRevision);
				var oldBlames = GetBlame(_leftRepo + relUrl, _leftRepoRevision);

				if (newBlames.Count != oldBlames.Count)
				{
					Console.WriteLine("	Count of lines mismatch: {0}", relUrl);
					return false;
				}

				for (var i = 0; i < oldBlames.Count; i++)
				{
					var oldLine = oldBlames[i];
					var newLine = newBlames[i];

					if (oldLine.Line != newLine.Line)
					{
						Console.WriteLine("	Lines mismatch: {0}", relUrl);
						return false;
					}

					if (oldLine.LineNumber != newLine.LineNumber)
					{
						Console.WriteLine("	Line numbers mismatch: {0}", relUrl);
						return false;
					}

					if (oldLine.Author == newLine.Author)
						continue;

					Console.WriteLine("	Authors mismatch: {0}: {1} != {2}", relUrl, oldLine.Author, newLine.Author);
					return false;
				}
			}
			catch(Exception ex)
			{
				Tw.WriteLine(relUrl);
				Tw.WriteLine("=>");
				Tw.WriteLine(ex);
			}

			return true;
		}

		static void SaveBlames(string relUrl)
		{
			try
			{
				var newBlame = GetBlame(_rightRepo + relUrl, _rightRepoRevision);
				var oldBlame = GetBlame(_leftRepo + relUrl, _leftRepoRevision);

				var sbOld = new StringBuilder();
				var sbNew = new StringBuilder();

				for (var i = 0; i < Math.Max(newBlame.Count, oldBlame.Count); i++)
				{
					if (i < newBlame.Count)
					{
						sbNew.AppendFormat("{0,-20}: {1}", newBlame[i].Author, newBlame[i].Line);
						sbNew.AppendLine();
					}

					if (i < oldBlame.Count)
					{
						sbOld.AppendFormat("{0,-20}: {1}", oldBlame[i].Author, oldBlame[i].Line);
						sbOld.AppendLine();
					}
				}

				SaveBlame("right", relUrl, sbNew.ToString());
				SaveBlame("left", relUrl, sbOld.ToString());
			}
			catch (Exception ex)
			{
				Console.WriteLine("ERROR: {0}", ex.Message);
			}
		}

		static void SaveBlame(string side, string relUrl, string blameText)
		{
			var path = Path.Combine("_compare", side, relUrl) + ".txt";
			Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
			File.WriteAllText(path, blameText);
		}

		static Collection<SvnBlameEventArgs> GetBlame(string url, int revision)
		{
			try
			{
				var blameArgs = new SvnBlameArgs {
					RetrieveMergedRevisions = false,
					IgnoreLineEndings = false,
					IgnoreMimeType = false,
					IgnoreSpacing = SvnIgnoreSpacing.None
				};

				Collection<SvnBlameEventArgs> blameEvents;
				SvnClient.GetBlame(new SvnUriTarget(url, new SvnRevision(revision)), blameArgs, out blameEvents);

				return blameEvents;
			}
			catch (Exception ex)
			{
				if (ex is SvnFileSystemNodeTypeException || ex.InnerException is SvnFileSystemNodeTypeException || ex is SvnEntryNotFoundException || ex is SvnClientBinaryFileException || ex.InnerException is SvnClientBinaryFileException)
					return new Collection<SvnBlameEventArgs>();

				throw;
			}
		}
	}
}
