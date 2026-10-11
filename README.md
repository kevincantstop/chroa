# Chroa Video Player

A minimal, frameless video player for Windows, built on libmpv and Avalonia UI.

## License

Chroa Video Player
Copyright (C) 2026 kevincantstop

This program is free software: you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation, either version 2 of the License, or (at your option) any later
version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with
this program. If not, see <https://www.gnu.org/licenses/>.

See [LICENSE](LICENSE) for the full text.

### Third-party components

This repository ships no third-party binaries: the folder the player loads its
native library from is excluded from version control. Released builds do bundle
the following, each under its own license:

- **libmpv / mpv** — GPLv2+, Windows build by
  [shinchiro](https://github.com/shinchiro/mpv-winbuild-cmake), version
  `v0.41.0-1115-gb2c255c13`; upstream source
  [mpv-player/mpv](https://github.com/mpv-player/mpv).
- **ANGLE** (`av_libglesv2.dll`) — BSD-3-Clause, Copyright 2018 The ANGLE
  Project Authors.
- **SkiaSharp**, **HarfBuzzSharp** — MIT, Copyright (c) 2015-2016 Xamarin, Inc.
  and Copyright (c) 2017-2018 Microsoft Corporation.
- **Inter** font, embedded in `Avalonia.Fonts.Inter.dll` — SIL Open Font
  License 1.1, Copyright (c) 2016 The Inter Project Authors
  (<https://github.com/rsms/inter>).
- **Avalonia UI**, **CommunityToolkit.Mvvm**, **HanumanInstitute.LibMpv** and the
  remaining managed dependencies — MIT.
