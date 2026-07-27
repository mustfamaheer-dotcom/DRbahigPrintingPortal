========================================
  PrintingSystem - Local Print Agent
  BookShopPrintPortal
========================================

WHAT IS THIS?
This program runs on your Windows computer and listens for
print jobs from the online book portal. When you click "Print"
on the website, this agent downloads, decrypts, and sends the
PDF directly to your local printer.

------------------------------
INSTALLATION
------------------------------

1. Create a folder on your computer (e.g., C:\BookShopTools).

2. Place these files in that folder:
   - PrintingSystem.exe
   - appsettings.json

3. Open appsettings.json in Notepad and verify the settings:
   - BaseUrl: Should be https://drbaheegbook.runasp.net
   - OwnerPassword: Must match the server's owner password
   - DefaultPrinterName: Leave empty "" to use your default printer,
     or type the exact USB printer name.

4. Double-click PrintingSystem.exe to start.

5. A console window will appear showing:
   "[PrintingSystem] Listening on http://localhost:8080"
   Keep this window open while you print.

6. Go to the website, open a book, and click "Print".

------------------------------
TROUBLESHOOTING
------------------------------

Problem: "Failed to open printer" error
  -> Make sure your printer is turned on and connected via USB.
  -> In appsettings.json, set DefaultPrinterName to your printer's
     exact name (found in Windows Settings > Printers).

Problem: Firewall warning
  -> Click "Allow access" when Windows Firewall prompts you.

Problem: Agent won't start, port 8080 in use
  -> Another program is using port 8080. Close that program or
     change the port in the source code.

Problem: "Download failed" error
  -> Check your internet connection.
  -> Verify BaseUrl in appsettings.json is correct.

------------------------------
RUNNING IN BACKGROUND (Optional)
------------------------------
If you don't want the console window showing, contact your
administrator to provide a version with WinExe output type.
The agent will then run silently in the background.

------------------------------
UNINSTALL
------------------------------
Simply delete the folder containing PrintingSystem.exe and
appsettings.json. No registry changes are made.
