using System.IO;
using System.Windows.Shapes;

namespace ReciteHelper.Utils;

public static class Extensions
{
    extension(string str)
    {
        public char FindNextChar(int index)
        {
            for (int i = index; i < str.Length; i++)
                if (!char.IsWhiteSpace(str[i])) return str[i];
            return ' ';
        }
    }

    extension(Directory)
    {
        /// <summary>
        /// Deletes all files in the specified directory.
        /// </summary>
        /// <remarks>This method removes all files directly within the specified directory but does not
        /// delete subdirectories or their contents. If a file cannot be deleted (for example, due to being in use), the
        /// method continues processing the remaining files.</remarks>
        /// <param name="targetDirectory">The full path of the directory whose files are to be deleted. Cannot be null or an empty string.</param>
        public static void Clear(string targetDirectory)
        {
            var files = Directory.GetFiles(targetDirectory, "*");
            foreach (string file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // ignored
                    Console.WriteLine($"无为在歧路，儿女共沾巾。");
                }
            }
        }
    }
}
