# CoreRation

## What is it?

CoreRation is a basic tool which was created to simplify the process of assigning core affinities and process priorities to the currently running processes. It allows for multiple presets to be saved and applied quickly.

## Why should I use it?

Unless you're certain that you need it, you probably shouldn't. In the vast majority of cases, the operating system will do a perfectly adequate job of balancing process load, and trying to micromanage your system may result in worse performance than you would have otherwise. However, for gaming,streaming, or other high-load real-time processing, it *may* be useful if used carefully.

## Why was this made?

While attempting to switch from GPU to CPU encoding for streaming, I was trying to find a way to help ensure that the CPU encoding didn't stutter when the game being played was CPU-heavy. So this utility was created to allow me to assign specific CPU affinities and process priorities to specific processes, and then assign a default set of affinities to all other processes.

It didn't work out in my case, and I ultimately still use GPU encoding--but that wasn't a fault of this utility, which could still potentially be useful to others who want to give it a go.

## What's missing?

- [x] Background monitoring for ensuring that newly-created processes are assigned the correct affinities.
- [ ] Better reset functionality (i.e. restore to original priorities and affinities, rather than just resetting all affinities to all cores).
- [ ] Applying settings to child processes of one of the specified processes.

## Also Consider:

* [NotCPUCores](https://github.com/rcmaehl/NotCPUCores) - A similar tool. I tried this one initially, but found that I wanted more control than it offers, which is what inspired me to create this tool instead.
