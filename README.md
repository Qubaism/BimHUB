# BimHUB

BimHUB will be a Revit add-in by QBTools. It will provide a dockable panel for browsing, searching, previewing, and inserting BIM library content from an organized local or shared library directly into a Revit project.

The goal of BimHUB is to make reusable BIM content easier to find, understand, and place in Autodesk Revit. Instead of manually browsing folders or relying on memory, users should be able to search a structured library, preview available content, and insert the right item into the active Revit project with confidence.

## Who This Is For

BimHUB is intended for BIM users and teams who maintain reusable Revit content, including:

- BIM coordinators managing standard families and details
- Architects, engineers, and modelers who regularly reuse project content
- Revit users who want a cleaner way to browse and insert library items
- Small teams building their own BIM standards and content library
- Developers learning how to build practical Revit add-ins

## Product Goals

- Provide a dockable Revit panel for browsing BIM library content
- Support search and filtering so users can find content quickly
- Show useful previews and metadata before insertion
- Insert selected content into the active Revit project
- Support organized local and shared library locations
- Keep the project open, transparent, and useful for learning
- Build the tool from a clean public codebase with no private project knowledge

## Planned Features

- Revit ribbon button for opening BimHUB
- Dockable Revit panel interface
- Library browser for Revit families and standard details
- Search by name, category, type, and tags
- Preview panel for selected content
- Metadata display for file path, category, version, and notes
- Insert/load selected Revit families into the current model
- Configurable library locations
- Documentation for setup, development, and contribution

## Project Status

BimHUB is currently in the initial planning and public setup stage. No installer or production-ready release is available yet.

Early development priorities:

1. Create the base Revit add-in structure
2. Add a minimal Revit ribbon entry
3. Add a dockable panel shell inside Revit
4. Build the first library browsing interface
5. Add search and preview support
6. Add safe family loading and insertion workflows

## Principles

- Public-first code and documentation
- No private company data, project files, or internal workflows
- Clear structure for learning and future contribution
- Small reviewed changes instead of large unverified uploads

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.
