using System.Text.RegularExpressions;

namespace SoftwareDock.Domain.Applications;

/// <summary>
/// An uninstaller command line as registered by the application's installer, split into the
/// executable and its arguments.
/// </summary>
/// <remarks>
/// Installers write <c>UninstallString</c> in every shape imaginable: quoted, unquoted with
/// spaces in the path, <c>rundll32</c> one-liners, MSI product codes. Parsing it once, here,
/// keeps that mess out of the code that runs it.
/// </remarks>
public sealed partial record UninstallCommand
{
    private const string MsiExec = "msiexec.exe";

    private UninstallCommand(string fileName, string arguments, string? msiProductCode)
    {
        FileName = fileName;
        Arguments = arguments;
        MsiProductCode = msiProductCode;
    }

    /// <summary>Executable to start.</summary>
    public string FileName { get; }

    /// <summary>Arguments exactly as registered, to be passed through verbatim.</summary>
    public string Arguments { get; }

    /// <summary>The product code when this is a Windows Installer package; otherwise <see langword="null"/>.</summary>
    public string? MsiProductCode { get; }

    public bool IsWindowsInstaller => MsiProductCode is not null;

    /// <summary>
    /// Parses a registry <c>UninstallString</c>/<c>QuietUninstallString</c>. Returns
    /// <see langword="false"/> for blank or unparseable values instead of throwing: a broken
    /// entry is a fact about someone else's installer, not a bug in ours.
    /// </summary>
    public static bool TryParse(string? commandLine, out UninstallCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        var text = commandLine.Trim();
        var (fileName, arguments) = SplitExecutable(text);
        if (fileName.Length == 0)
        {
            return false;
        }

        string? productCode = null;
        var executable = WindowsFileName(fileName);
        if (executable.Equals(MsiExec, StringComparison.OrdinalIgnoreCase)
            || executable.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var match = ProductCodePattern().Match(arguments);
            if (match.Success)
            {
                productCode = match.Value.ToUpperInvariant();
                fileName = MsiExec;
            }
        }

        command = new UninstallCommand(fileName, arguments, productCode);
        return true;
    }

    /// <summary>
    /// The command to run. For Windows Installer packages the registered verb is frequently
    /// <c>/I</c> (which opens the <em>repair</em> dialog), so it is always rewritten to <c>/X</c>;
    /// <paramref name="silent"/> adds <c>/qn /norestart</c>. Other uninstallers are returned
    /// untouched — their silent switches are not ours to guess.
    /// </summary>
    public (string FileName, string Arguments) ToProcessCommand(bool silent)
    {
        if (MsiProductCode is null)
        {
            return (FileName, Arguments);
        }

        var arguments = $"/X{MsiProductCode}";
        return (MsiExec, silent ? $"{arguments} /qn /norestart" : arguments);
    }

    /// <summary>
    /// File name of a Windows path. <see cref="Path.GetFileName(string)"/> only knows the separators
    /// of the OS it runs on, and this model is about Windows paths wherever it runs.
    /// </summary>
    private static string WindowsFileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    /// <summary>
    /// Separates the executable from its arguments. A quoted path is taken as-is; an unquoted one
    /// is cut right after the first <c>.exe</c> that ends a token, which is how Windows resolves
    /// <c>C:\Program Files\App\uninstall.exe /S</c> as well.
    /// </summary>
    private static (string FileName, string Arguments) SplitExecutable(string text)
    {
        if (text[0] == '"')
        {
            var closing = text.IndexOf('"', 1);
            return closing < 0
                ? (text.Trim('"'), string.Empty)
                : (text[1..closing], text[(closing + 1)..].Trim());
        }

        var exe = ExecutableEndPattern().Match(text);
        if (exe.Success)
        {
            var end = exe.Index + exe.Length;
            return (text[..end], text[end..].Trim());
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..].Trim());
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex ProductCodePattern();

    [GeneratedRegex(@"\.exe(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutableEndPattern();
}
