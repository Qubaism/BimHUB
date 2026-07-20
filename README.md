# BimHUB

BimHUB will be a Revit add-in by QBTools. It will provide a dockable panel for browsing, searching, previewing, and inserting BIM library content from organized local or shared libraries directly into a Revit project.

The goal of BimHUB is to make reusable BIM content easier to find, understand, and place in Autodesk Revit. Instead of manually browsing folders or relying on memory, users will be able to search a structured library, preview available content, and insert the right item into the active Revit project with confidence.

## Who This Is For

BimHUB is intended for BIM users and teams who maintain reusable Revit content, including:

- BIM coordinators managing standard families and details
- Architects, engineers, and modelers who regularly reuse project content
- Revit users who want a cleaner way to browse and insert library items
- Small teams building their own BIM standards and content libraries
- Developers learning how to build practical Revit add-ins

## Current Development Scope

The current development build includes:

- A QBTools ribbon panel and BimHUB command
- A dockable Revit library panel
- Multiple configurable Revit family and details folders
- Family search and category filtering
- List and grid layouts with adaptive card sizing
- Family thumbnails, preview windows, metadata, and available types
- Favorites and recently used content
- Family insertion by double-click or drag and drop
- Drafting-view and drafting-sheet browsing
- Drafting-view and sheet previews generated from compatible source projects
- Drafting-view import into the active project
- Drafting-sheet import with its placed drafting views
- Local catalog caching and background preview generation

BimHUB only opens source content that matches the active Revit version. Source families and detail projects are not upgraded or saved by the add-in.

## Revit Compatibility

| Revit version | Build target | Current status |
|---|---|---|
| Revit 2023 | .NET Framework 4.8 | Build, automated tests, and in-Revit testing pass |
| Revit 2024 | .NET Framework 4.8 | Build, automated tests, and in-Revit testing pass |
| Revit 2025 | .NET 8 | Build, automated tests, and in-Revit testing pass |
| Revit 2026 | .NET 8 | Build, automated tests, and in-Revit testing pass |
| Revit 2027 | .NET 8 configuration | Requires the Revit 2027 API before it can be built and verified |

Revit 2023 through Revit 2026 have been validated individually. Compatibility will continue to be checked before each public release.

## Product Goals

- Make reusable Revit content fast to find and preview
- Support organized local and shared library locations
- Insert families, drafting views, and drafting sheets safely
- Keep library browsing responsive for larger content collections
- Preserve source files without automatic upgrading or saving
- Keep the public codebase independent of private project knowledge

## Project Status

BimHUB is under active development. The source code is available for review, but no public installer or production-ready release is available yet.

Before the first release, the project will require:

- Continued regression testing across supported Revit versions
- Additional stability and performance testing with larger libraries
- Installation and update packaging
- User installation guidance
- A versioned GitHub release

See the [product roadmap](docs/roadmap.md) for the current development direction.

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.
