# External Code: NetPad Dump Implementation

This directory contains code ported from the [NetPad](https://github.com/tareqimbasher/NetPad) project.

## Purpose
We have integrated NetPad's powerful HTML-based `Dump` serialization logic to provide rich, visual object inspection within our scratchpad, surpassing the capabilities of standard console-based tools.

## Source & Credits
- **Original Project**: NetPad
- **Author**: Tareq Imbasher
- **Source Link**: [NetPad GitHub](https://github.com/tareqimbasher/NetPad)
- **License**: [MIT License](https://github.com/tareqimbasher/NetPad/blob/main/LICENSE)

## Modifications
To ensure compatibility with this project, the following changes were made:
-   **Integrated O2Html**: Included the [O2Html](https://github.com/tareqimbasher/O2Html) library (also by Tareq Imbasher) directly to handle object serialization to HTML.
-   **Namespace Adjustments**: Replaced original namespaces with `ScratchpadSharp.Core.External...` to fit the local architecture.
-   **Dependency Removal**: Removed dependencies on ASP.NET Core specific libraries.
-   **Resource Simplification**: Simplified resource loading for local Avalonia integration.
-   **Memory Management**: Refactored `O2Html`'s `HtmlSerializer` to use instance-based caching instead of static caches to support `AssemblyLoadContext` unloading and prevent memory leaks.

## License
This code is used under the terms of the MIT License. A copy of the license is included below:

```text
Copyright (c) 2020 Tareq Imbasher

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
... 