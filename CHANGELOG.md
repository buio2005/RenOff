# Changelog

All notable changes to RenOff are documented in this file.

## [1.0.1] - 2026-08-13

### Fixed

- **App lock no longer triggers while you are working.** Inactivity was measured
  only from the main window's mouse and keyboard events, so using another app
  counted as being idle and RenOff locked itself out of the blue. Idle time is
  now read from the system (last input anywhere on the machine).
- **The lock screen has a way out.** It could only be dismissed with the password
  or the recovery code, so anyone without either had to kill the process from
  Task Manager. There is now a "Quit RenOff" button.
- **Killing the process is no longer a bypass.** The locked state is persisted,
  so RenOff comes back locked. If the window is hidden in the tray the lock
  happens silently and the lock screen appears the next time you open the app.
- **Reminders are no longer consumed just by being shown.** A due reminder was
  marked as fired the moment it was read from the database: letting the pop-up
  time out, or clicking it to open the app, burned the reminder and its snooze
  buttons with it. A reminder is now closed only by snooze, by Done, or by
  opening the app from the pop-up; an ignored one comes back after 10 minutes.
- **The reminder pop-up and the reading nudge no longer look identical.** They
  shared the same title in both languages, which made the nudge look like a
  reminder that had lost its snooze buttons. The reminder pop-up is now titled
  "Promemoria di una nota" / "Note reminder".
- **Modern style: the note title is no longer clipped.** The Modern `TextBox`
  template ignored `VerticalContentAlignment`, so the title was cut off inside
  its fixed-height box.
- **Modern style: the reminder buttons are no longer cut off.** Save, Snooze and
  Disable now sit on their own right-aligned line instead of overflowing the
  panel.

### Changed

- Tray balloons and pop-ups are suppressed while the app is locked, so note
  titles don't leak on screen and no reminder is spent while it can't be acted on.
- The Modern UI refresh from the pre-1.0 line (flat checkboxes, thin scrollbars,
  card and pop-up shadows, refreshed Light/Dark brushes) is now part of the
  released code.

## [1.0.0] - 2026-07-07

First full stable release: notes and to-dos, reminders with snooze, reading
nudges, tray integration, Light/Dark themes, Classic/Modern styles, Italian and
English, drag & drop reorder, plain and encrypted backup export/import, optional
app lock with recovery code.
