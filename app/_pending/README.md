# _pending

Parked work-in-progress that is **not** part of the solution
(`AvellSucks.Replacement.slnx`) and is not compiled by any project.

## rgb-hid/

The ITE HID keyboard-lighting backend the RGB tab is waiting on. The shipped app
uses a stub (`LocalRgbService`) because the RGB feature can't be tested — see the
"why RGB is untested" / "The keyboard" section of the top-level README. This code
lands here until there's working keyboard hardware to verify it against; when it
is wired in, it becomes a real project referenced by the solution.
