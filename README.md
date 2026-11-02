# Madi AI — native Windows desktop starter

The interface is built in **native WPF / .NET 8**, not HTML or Electron. The supplied futuristic dark-blue and cyan Madi AI image is the visual direction.

**Current starter features:** animated holographic orbit interface; dark command dashboard; first-run secret-code enrollment and later verification with a salted PBKDF2 hash (code not stored in GitHub); spoken "Tell me your secret code to open me" greeting and "Welcome, Boss Hammad" after unlock; typing commands to open basic Windows apps, web services and a chosen file; arithmetic; basic chat greetings. No paid API is required for these basic actions.

**Not yet working:** general AI chat, reliable voice-command recognition, native computer vision, MT5 trading, automatic eBay/Canva/AliExpress operations, WhatsApp messaging or AI voice calls, complete system settings control, and full dark custom installer wizard. These require additional integration and testing. Do not enable high-impact actions without safeguards.

## Windows installation

A GitHub Actions workflow builds a self-contained Windows app and NSIS installer. Open **Actions → Build Native Madi AI**, select a successful run and download the **Madi-AI-Windows-Installer** artifact. Extract the archive and launch `Madi-AI-Setup.exe`. This is an unsigned development installer, not a tested production release. On first start, choose your desired secret code (six or more characters). This avoids checking passwords into a public repository.

## Developer build

Requires .NET SDK 8 on Windows:

```powershell
dotnet run --project MadiAI/MadiAI.csproj
```

We will iterate on the native UI and module integrations. No user credentials, payment details or trading keys belong in the repository.
