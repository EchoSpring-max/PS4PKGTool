# PS4 PKG Tool

[![Github All Releases](https://img.shields.io/github/downloads/pearlxcore/PS4-PKG-Tool/total.svg)](https://github.com/pearlxcore/PS4-PKG-Tool/releases/latest)
[![License](https://img.shields.io/github/license/pearlxcore/PS4-PKG-Tool.svg)](LICENSE)
[![Stars](https://img.shields.io/github/stars/pearlxcore/PS4-PKG-Tool.svg)](https://github.com/pearlxcore/PS4-PKG-Tool)
[![Views](https://hits.sh/github.com/pearlxcore/PS4-PKG-Tool.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10-blue)]()

A windows application for managing and viewing your PS4 PKG collection.

Suggestions are welcome. Report any bugs [here](https://github.com/pearlxcore/PS4-PKG-Tool/issues).

**This is not software for obtaining free PS4 games.**

# Support My Work

[![ko-fi](https://github.com/user-attachments/assets/be9cc4be-3352-4bd5-8086-05c30930f81d)](https://ko-fi.com/R6R524N7X)

[![paypal](https://user-images.githubusercontent.com/36906814/102657760-39d1ce00-41b1-11eb-96fe-c10e2d9b3f39.png)](https://www.paypal.com/paypalme/pearlxcoree)

# Requirement

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

# Features

- Fast startup with manifest cache. Scan once, load instantly every time after.
- Drag and drop PKG folders onto the window to load them.
- Grouped list view. Group your library by Title, Title ID, Category, System Version, or PKG Type.
- Title column visible in the main grid so game names are shown without clicking.
- Rename PKG by install priority. Files get sequence prefixes: 00 - Base, 01 - Update.
- Rename PKG using naming presets or a custom format with placeholders.
- Move and organize PKGs into folders by title, category, region, or type.
- View PKG information: param.sfo, trophy list, entries, and change info.
- View and extract PKG data including background images and icons.
- Trophy metadata viewer with names, descriptions, and grades.
- Latest Update column shows the newest available update version per PKG.
- Download official update PKGs from a standalone form.
- Filter the grid by category (Game, Patch, Addon, App) or search by filename, title, or ID.
- Set backport labels and check for duplicate PKGs.
- Export your PKG list to Excel.
- Send PKGs to PS4 over the network using Remote Package Installer.
- Check PS5 backward compatibility.

# How to use Remote Package Installer

This is only compatible with PS4 firmware that can run Flatz's Remote Package Installer app. Split update PKG files are currently not supported.

- Open the program settings.
- Set the IP addresses for your PC and PS4.
- Install Node.js and the http-server module (ensure that Node.js is allowed through the firewall).
- If you are unable to install the http-server module via the PS4 PKG Tool, try restarting the tool and reinstalling the module. Alternatively, run `npm install http-server -g` manually in the command[...]
- Save the changes and exit the program settings.
- Launch the Remote Package Installer app on your PS4.
- Select the PKG file you wish to install, right-click on it, and choose 'Send PKG to PS4'.

# Screenshot

![Image 1](https://github.com/user-attachments/assets/e0d71e99-b892-4762-a656-e16ae62cf70d)

![Image 2](https://github.com/user-attachments/assets/1ebf5e56-50de-4457-90ff-4e4f9c49f623)

![Image 3](https://github.com/user-attachments/assets/bf14767a-e341-4c98-9308-78b4cf2529ee)

![Image 4](https://github.com/user-attachments/assets/767a1b8c-c3f6-42d8-8314-04fcfb298918)

![Image 5](https://github.com/user-attachments/assets/0524111a-6140-4d74-b467-170e8772703b)

![Image 6](https://github.com/user-attachments/assets/130edeef-47ee-4346-892f-7113bc0a8de6)

# Download

[**Download Latest Release**](https://github.com/pearlxcore/PS4-PKG-Tool/releases/latest)

# License

[GPL-3.0](LICENSE)

# Credit

- [Robin Perris](https://github.com/RobinPerris) (DarkUI)
- [xXxTheDarkprogramerxXx](https://github.com/xXxTheDarkprogramerxXx)
- [Maxton (RIP)](https://github.com/maxton)
- [leecherman](https://sites.google.com/site/theleecherman/)
- [andshrew](https://github.com/andshrew)
- Sony <3
