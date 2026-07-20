using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QBTools.BimHUB.Compatibility;

internal static class RuntimeCompatibility
{
    public static int ProcessId => Process.GetCurrentProcess().Id;

    public static bool ContainsIgnoreCase(string value, string search)
    {
        return value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }

    public static string TrimEndingDirectorySeparator(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        int minimumLength = root.Length;
        int length = path.Length;
        while (length > minimumLength
               && (path[length - 1] == Path.DirectorySeparatorChar
                   || path[length - 1] == Path.AltDirectorySeparatorChar))
        {
            length--;
        }

        return length == path.Length ? path : path.Substring(0, length);
    }

    public static IEnumerable<string> EnumerateFilesRecursively(string root, string searchPattern)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        while (pendingDirectories.Count > 0)
        {
            string directory = pendingDirectories.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly); }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            foreach (string file in files) yield return file;

            string[] children;
            try { children = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly); }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            foreach (string child in children) pendingDirectories.Push(child);
        }
    }

    public static TValue GetValueOrDefault<TKey, TValue>(
        IDictionary<TKey, TValue> values,
        TKey key,
        TValue defaultValue = default!)
    {
        return values.TryGetValue(key, out TValue? value) ? value : defaultValue;
    }

    public static string GetSha256Prefix(string value, int characterCount)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        string hex = BitConverter.ToString(hash).Replace("-", string.Empty);
        return hex.Substring(0, Math.Min(characterCount, hex.Length));
    }

    public static string GetRelativePath(string relativeTo, string path)
    {
#if NETFRAMEWORK
        string basePath = Path.GetFullPath(relativeTo);
        if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            basePath += Path.DirectorySeparatorChar;
        }

        var baseUri = new Uri(basePath, UriKind.Absolute);
        var pathUri = new Uri(Path.GetFullPath(path), UriKind.Absolute);
        if (!string.Equals(baseUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }

    public static void MoveFileReplacing(string sourcePath, string destinationPath)
    {
#if NETFRAMEWORK
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        File.Move(sourcePath, destinationPath);
#else
        File.Move(sourcePath, destinationPath, true);
#endif
    }

    public static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
#if NETFRAMEWORK
        if (process.HasExited) return;

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExited(object? sender, EventArgs args) => completion.TrySetResult(null);

        process.EnableRaisingEvents = true;
        process.Exited += OnExited;
        using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
        {
            if (process.HasExited) completion.TrySetResult(null);
            try
            {
                await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                process.Exited -= OnExited;
            }
        }
#else
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    public static void KillProcess(Process process)
    {
#if NETFRAMEWORK
        process.Kill();
#else
        process.Kill(entireProcessTree: true);
#endif
    }
}
