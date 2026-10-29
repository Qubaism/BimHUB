# BimHUB Roadmap

BimHUB will be developed in small, reviewable stages. Priorities may change as workflows are tested and new requirements are identified.

## Current Development

The current development build provides the main product foundation:

- Revit add-in projects for .NET Framework 4.8, .NET 8, and .NET 10
- Version- and runtime-specific builds for Revit 2023 through Revit 2027
- QBTools ribbon panel and BimHUB command
- Dockable library panel
- Multiple family and details library locations
- Search, category filters, list view, and grid view
- Family previews, metadata, available types, favorites, and recent items
- Family loading by double-click and drag and drop
- Drafting-view and drafting-sheet catalogs
- Drafting-view and drafting-sheet import
- Background family thumbnail and details preview generation
- Local catalog caching for faster startup and navigation

## Compatibility Validation

- Previous hands-on workflow testing completed in Revit 2023 through Revit 2026
- Current Revit 2024, Revit 2025.4, Revit 2026.5, and Revit 2027 targets build and pass automated tests
- Live workflow retesting is required for the refreshed compatibility builds
- Continue cross-version regression and stability testing
- Build and test Revit 2025.5 on a matching installation
- Rebuild the Revit 2023 and pre-2026.5 targets with their matching API references
- Confirm family, drafting-view, and drafting-sheet behavior in every supported version

## Product Stabilization

- Drafting-view copy now skips grouped, callout, cross-view, and individually unsupported elements instead of displaying a blocking Revit paste error; code and builds are verified, live Revit retest pending
- Drafting-sheet import now activates the completed sheet and closes only the drafting views opened during that import; code and builds are verified, live Revit retest pending
- Test large local and shared network libraries
- Improve error reporting for unavailable or incompatible content
- Validate background indexing and cancellation during long operations
- Test duplicate families, types, views, and sheet numbers
- Confirm settings, favorites, and recent items survive Revit restarts
- Expand automated tests around catalog, filtering, cache, and import preparation logic

## Release Readiness

- Define the first release feature set
- Continue testing the per-user installer across clean and upgraded systems
- Digitally sign production installers
- Complete final regression testing
- Publish versioned GitHub releases with installer checksums

## Future Possibilities

- Additional library metadata and custom tags
- Team-managed library standards
- Content usage information
- More BIM content formats
- Optional centralized catalog management
