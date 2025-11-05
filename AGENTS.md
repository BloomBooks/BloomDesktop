This project has a web front-end at src/BloomBrowserUI. That has its own AGENTS.md file.

# Code Style

- Avoid removing existing comments.
- Avoid adding a comment like "// add this line".


# Testing

- Fail Fast. Don't write code that silently works around failed dependencies. If a dependency is missing we should fail. Javascript itself will fail if we try to use a missing dependency, and that's fine. E.g. if you expect a foo to be defined, don't write "if(foo){}". Just use foo and if it's null, fine, we'll get an error, which is good.


# Terminal
The vscode terminal often loses the first character sent from copilot agents. So if you send "cd" it might just say "bash: d: command not found". Try prefixing commands with a space.

# Don't run build
It is vital that you not run `yarn build` unless instructed to. If there is already a "--watch" build running, you will wreck it and waste the developer's time. You are welcome to `yarn lint` if you want to check for errors without building.
