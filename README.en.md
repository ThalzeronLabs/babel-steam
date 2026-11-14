<p align="center">
  <img src="docs/images/hero.en.jpg" alt="Babel for Steam: the whole of Steam in Brazilian Portuguese" width="100%">
</p>

# Babel for Steam: the whole of Steam in Brazilian Portuguese

[![CI](https://github.com/ThalzeronLabs/babel-steam/actions/workflows/ci.yml/badge.svg)](https://github.com/ThalzeronLabs/babel-steam/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ThalzeronLabs/babel-steam?label=download&color=4FC3F7&labelColor=0F1923)](https://github.com/ThalzeronLabs/babel-steam/releases/latest)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-blue)](LICENSE)

[Português (Brasil)](README.md) · **English**

Steam's menus already come in Portuguese. What does not is everything **people and studios write**: patch notes,
events, reviews, discussions, guides, Workshop descriptions and many store pages. **Babel** translates all of that into
Brazilian Portuguese **on the spot, inside the Steam window**, with the user's own free Gemini API key.

- **Two-minute setup:** download, open, paste the key. No Python, no command line, no administrator rights.
- **Speaks gamer:** buff, nerf, farm, drop and GG stay the way Brazilian players say them; irony, slang and swearing stay too.
- **Never pays twice:** each piece of text is translated once and kept on the PC. Opening it again costs nothing.
- **Does not break Steam:** text is replaced in place, without touching the page structure. Alt + click shows the original.

> Babel for Steam is independent and not affiliated with Valve. Read the [disclaimer](#disclaimer-security-and-privacy) before using it.

## How it works

1. **The debugging port.** The Steam client is a Chromium browser inside (CEF). Started with its documented launch
   options `-cef-enable-debugging -devtools-port=9333`, it lets a program **on the same PC** (127.0.0.1 only) talk to its
   pages over the Chrome DevTools Protocol.
2. **The injector** checks the open pages every 1.5 s and adds the page script to each one. Page and app talk through a
   CDP *binding*: a direct channel, with no network in between.
3. **The page script** finds runs of text, skips Portuguese, game titles, URLs and short labels, and asks only for what is
   on screen (plus the next few blocks, so scrolling arrives translated). It swaps text **in place**, keeping bold,
   links and lists through `<t1>…</t1>` markers, so React views (library, chat) keep working.
4. **Translation** joins requests from every page into batches. The **SQLite cache** answers first, keyed by a SHA-256
   of the text and a version of the instructions; only new text goes to Gemini, which picks the newest Flash-Lite model
   at start-up and follows a [Brazilian gamer glossary](src/Babel/Web/glossario.txt). A failing text splits its batch
   until it is isolated, and a page cannot ask for more than 400,000 characters a minute. When Google refuses because
   of the account itself (the prepaid credit of a billing-enabled project is used up, or the quota is reached), Babel
   stops asking for a minute and says so in the tray. The key test in the setup translates one word, so a key with no
   credit is caught before you finish.

If Steam is opened another way (a desktop shortcut), Babel reopens it with the port, but only with no game running,
after 45 s of stability and at most once every 10 minutes.

## Disclaimer, security and privacy

> **Disclaimer.** Babel is an independent project and is not affiliated with, sponsored or endorsed by Valve. Steam and the Steam logo are trademarks and/or registered trademarks of Valve Corporation in the U.S. and/or other countries. Babel uses a debugging option built into the Steam client and changes the text shown on screen. It doesn't change Steam's program files, doesn't touch your games and never acts on your account by itself. With your OK, the installer changes Steam's shortcut and startup entry so it opens with that option, and the uninstaller reverts it. The Steam Subscriber Agreement forbids modifying the client without Valve's permission, and Valve may restrict accounts. **Use at your own risk.**
>
> **Security.** While Steam is open with the debugging option, whether or not Babel is running, any program on your computer can control your logged-in Steam, including making purchases with your wallet (the port only answers on this PC, 127.0.0.1). Use Babel only on a computer you trust, and start Steam normally when you don't need translation. Babel never translates account, wallet, payment or login pages.
>
> **Gemini.** You use your own Google AI Studio key and accept [Google's terms](https://ai.google.dev/gemini-api/terms); the quota and the bill are yours. The Gemini API requires you to be 18 or older. On the free tier, Google uses what you send to improve its products, and human reviewers may read it, so don't send personal information on that tier. In the EU, Switzerland and the UK, only paid keys are allowed. Chat translation is off by default; turn it on only with a billing-enabled key. Babel has no server: nothing goes through me.

**Safeguards in the code:** an allow list of pages (the main window, the store and the community, minus account,
wallet, payment, cart, login, trading, market, inventory and profile editing; every other client window, chat
included, stays out); the port must belong to `steamwebhelper.exe` from Steam's own folder; connections only to
127.0.0.1 on the configured port; exactly five DevTools commands (a test fails on any other); no automatic action on
the account; a per-page and an app-wide budget (200,000 characters a minute, 2 million a day); Steam reopening off by
default; the translation cache only on this PC, with a button to clear it.

## Build

```bash
dotnet test tests/Babel.Tests
node tests/web/pagina.test.mjs
dotnet publish src/Babel -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

C# (.NET 10, WPF), one self-contained exe. Tests cover the translation queue (batching, de-duplication, cache,
versioning, failure isolation, batch limits), model selection, key validation and DPAPI round-trip, Steam detection,
and, in JavaScript, the page script's Portuguese detector, title detector and markup parser.

## How I build this

I use AI assistance (Claude) while I build this project: for drafting, exploring options and writing tests. The banner
illustration was made with an image model. I review every change, use Babel every day on my own Steam, and I'm
responsible for every line.

Steam is a trademark of Valve Corporation. This project is not affiliated with Valve or Google.

## License

Apache-2.0 © Esdras Souza (Thalzeron Labs). See [LICENSE](LICENSE).
