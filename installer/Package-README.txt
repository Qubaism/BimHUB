BimHUB for Windows
==================

BimHUB is a QBTools add-in for browsing and inserting Revit families,
drafting views, and drafting sheets from local or shared BIM libraries.

PACKAGE CONTENTS
----------------

- BimHUB-*-Setup.exe: Installs BimHUB for supported Revit versions.
- BimHUB-*-Setup.exe.sha256: Verifies the installer download.
- LICENSE.txt: GPL-3.0 license.

INSTALLATION
------------

1. Extract this ZIP file.
2. Close all Autodesk Revit windows.
3. Run BimHUB-*-Setup.exe.
4. Select the detected Revit versions where BimHUB should be installed.
5. Start Revit and open BimHUB from the QBTools ribbon panel.

Administrator access is not required. The installer installs BimHUB only for
the current Windows user and does not overwrite unrelated Revit add-ins. It
supports compatible Revit 2023 through Revit 2027 installations. For Revit
2025 and Revit 2026, the installer detects the installed build and selects the
matching .NET 8 or .NET 10 payload automatically.

REPAIR AND UPDATE
-----------------

Run the same installer again to repair the current installation. To update
BimHUB, close Revit and run the installer from the newer BimHUB package. A
separate updater program is not required.

UNINSTALLATION
--------------

Open Windows Settings > Apps > Installed apps, find "BimHUB by QBTools", and
select Uninstall. The installer creates and registers the correct uninstaller
during installation, so no separate uninstaller file is required in this ZIP.

VERIFYING THE INSTALLER
-----------------------

Open PowerShell in this folder and run:

  Get-FileHash .\BimHUB-*-Setup.exe -Algorithm SHA256

Compare the result with the value in BimHUB-*-Setup.exe.sha256. Do not run the
installer if the values differ.

SUPPORT
-------

Repository: https://github.com/Qubaism/BimHUB
Issues:     https://github.com/Qubaism/BimHUB/issues
