# APS Optimizer tool for From The Depths
This tool provides a simple, user-friendly solver to easily generate density-optimized APS layouts for From The Depths.

## Features

*   Support for 3, 4, and 5-clip tetris.
*   Automatic turret layout templates, with the option to customize them for armour or extra space.
*   Hard and soft symmetry enforcement options, including both rotational and reflexive symmetry.
*   Export generated layouts directly to *From The Depths* prefab file (`.blueprint`) for immidiate use in-game.
*   Choose the resulting prefab height, mapped automatically to the respective blocks (loader/clip length variants).

## Requirements
*   **cryptominisat5:** The advanced SAT solver used by this tool.
    *   You need to download the `cryptominisat5` exe separately from its official source [here](https://github.com/msoos/cryptominisat/releases). Ensure the executable (`cryptominisat5.exe` on Windows) is available in your system's PATH or placed in the same directory as this tool.

## Usage
leaving this here until I get a release build working on windows

## Contributing

Contributions are welcome! If you'd like to help improve this tool, please feel free to:

*   **Report Issues:** If you find a bug *or* you have a suggestion, please open an issue using the [GitHub Issues tab](https://github.com/trk20/APS-Optimizer/issues). Provide as much detail as possible, including steps to reproduce the bug if applicable.
*   **Submit Pull Requests:** If you've fixed a bug or added a feature:
    1.  Pull the repository.
    2.  Create a new branch for your changes.
    3.  Make your changes and commit them.
    4.  Push to your branch.
    5.  Open a Pull Request against the main branch of this repository. Please provide a clear description of your changes.
    6.  Request a review - hopefully it will get reviewed fairly quickly, but be patient.

Note: for making and testing changes to the codebase, you'll need to be able to run and debug Uno Platform apps - refer to their documentation [here](https://platform.uno/docs/articles/get-started.html) for setting that up.

## Acknowledgements

*   Thanks to the developers of `cryptominisat5` for their powerful SAT solver
*   Thanks to **sascha** on stackoverflow for their fantastic [answer on a polynomio grid-packing question](https://stackoverflow.com/a/47934736) that served as the basis for this tool's core logic.
