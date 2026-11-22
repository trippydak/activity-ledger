namespace ActivityLedger.Installation;

/// <summary>Shell code is constant; Windows paths are passed as environment data.</summary>
public static class UninstallCleanup
{
    public const string Arguments = "/d /v:off /s /c \"\"%AL_REMOVE_SCRIPT%\"\"";

    public static IReadOnlyDictionary<string, string> EnvironmentFor(string executable, string directory,
        string script, int processId, string systemDirectory)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        foreach (var path in new[] { executable, directory, script, systemDirectory })
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
                throw new ArgumentException("Cleanup paths must be nonempty Windows paths without command delimiters.");
        return new Dictionary<string, string>
        {
            ["AL_REMOVE_EXE"] = executable,
            ["AL_REMOVE_DIR"] = directory,
            ["AL_REMOVE_SCRIPT"] = script,
            ["AL_PARENT_PID"] = processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["AL_TASKLIST"] = Path.Combine(systemDirectory, "tasklist.exe"),
            ["AL_FINDSTR"] = Path.Combine(systemDirectory, "findstr.exe"),
            ["AL_TIMEOUT"] = Path.Combine(systemDirectory, "timeout.exe")
        };
    }

    public const string Script = """
        @echo off
        setlocal DisableDelayedExpansion
        title Activity Ledger removal
        echo Finishing Activity Ledger removal. Your journal and password will be kept.
        :wait
        "%AL_TASKLIST%" /FI "PID eq %AL_PARENT_PID%" /NH 2>nul | "%AL_FINDSTR%" /R /C:"[ ]%AL_PARENT_PID%[ ]" >nul
        if errorlevel 1 goto remove
        "%AL_TIMEOUT%" /t 1 /nobreak >nul
        goto wait
        :remove
        set "AL_ATTEMPTS=0"
        :retry
        del /f /q "%AL_REMOVE_EXE%" 2>nul
        if not exist "%AL_REMOVE_EXE%" goto done
        set /a AL_ATTEMPTS+=1
        if %AL_ATTEMPTS% GEQ 15 goto failed
        "%AL_TIMEOUT%" /t 1 /nobreak >nul
        goto retry
        :done
        rmdir "%AL_REMOVE_DIR%" 2>nul
        echo Activity Ledger has been removed. Your journal data remains on this computer.
        goto cleanup
        :failed
        echo Windows could not remove the application file. Close Activity Ledger and remove it manually:
        echo "%AL_REMOVE_EXE%"
        pause
        :cleanup
        del /q "%~f0"
        """;
}
