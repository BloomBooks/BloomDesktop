`./show.sh` opens a browser with this component for you to play with, using mock data. see show-component.uitest.ts for the various states in which you can open the dialog. E.g. `./show.sh page-only-url`
`yarn scope --backend [exportName]` uses Bloom itself, which is expected to be running on port 8089 (equivalently, `yarn scope:bloom [exportName]`).
`./test.sh` runs playwright tests against mock data
