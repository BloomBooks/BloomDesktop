# Moving your Team Collection to the cloud

*This page is the working source of truth for this walkthrough until it moves to the
Bloom docs site. It covers migrating an existing folder-based Team Collection (for
example, one shared over Dropbox or a network drive) to a cloud Team Collection, and
inviting your team to it.*

> **This feature is experimental.** You'll need to turn on "Cloud Team Collections
> (experimental)" in Settings before any of the buttons below appear. Experimental
> features can change or have rough edges. If you run into trouble, use Bloom's
> **Report a Problem** button so the Bloom team can help.

## Who this is for

You already have a Team Collection that your team shares by putting a folder in
Dropbox (or a similar shared/synced folder), and you want to switch to Bloom's new
cloud-hosted Team Collections instead — no shared folder, no Dropbox account
required for your team, and everyone just needs an internet connection and a Bloom
account.

## Before you begin: everyone checks in first

**This step matters.** Before you disconnect your old Team Collection, make sure
every team member has checked in all of their work, and that nobody has a book
checked out. On the machine you'll use to create the new cloud collection (see
below), open Bloom, go to the Collection tab, and check that every book shows as
checked in (no lock icon).

Why this matters: when you move to the cloud, Bloom copies whatever is in your
*local* folder at that moment up to the cloud as the starting point for everyone.
If a book had unsaved local edits that were never checked in to the old shared
folder, that book's cloud copy won't have those edits. Checking in first (on every
team member's machine, into the *old* Team Collection) makes sure nothing is lost.

## Step 1: Turn on the experimental feature

1. Open Bloom and go to **Settings > Advanced Settings**.
2. Under **Experimental Features**, turn on **Cloud Team Collections (experimental)**.
3. Restart Bloom when prompted.

Do this on the computer of whoever will create the cloud collection. Each team
member who wants to join later will also need to turn this on (once, on their own
computer) before they can see or join a cloud Team Collection.

## Step 2: Disconnect ("un-team") the old shared-folder collection

Pick ONE computer to do this on — normally whoever manages the Team Collection.
This step only needs to happen once, on one machine; other team members don't need
to do anything to their own copies yet.

1. Make sure step "Before you begin" above is done: everyone has checked in, and no
   books are checked out.
2. Open the collection in Bloom.
3. Close Bloom.
4. In your file manager, open this collection's folder (it's normally a
   subfolder of Documents\Bloom, and shows a small Team Collection icon on
   Bloom's own collection-chooser screen).
5. Find the file named `TeamCollectionLink.txt` in that folder and delete it (or
   move it somewhere else, in case you want it back).
6. This collection is now an ordinary, un-shared local collection again. Your
   original shared folder (in Dropbox or wherever it lived) is untouched — you can
   clean it up later once your whole team has moved to the cloud.

If you skip this step and try to enable cloud sharing anyway, Bloom will refuse
with an error explaining that `TeamCollectionLink.txt` still links this collection
to the old shared folder, and telling you to delete it first — so it's safe to try
even if you're not sure whether this step already happened.

## Step 3: Turn your local collection into a cloud Team Collection

1. Open the (now un-teamed) collection in Bloom.
2. Go to **Settings > Team Collection**.
3. Click **Share this collection on the Bloom sharing server (experimental)**.
4. Sign in with your Bloom account if you're not already signed in.
5. Confirm the collection name — this can't be changed later, so make sure it's
   right.
6. Bloom uploads your books to the cloud. This can take a while for a large
   collection; you'll see a progress bar. When it's done, you're the collection's
   administrator.

At this point, only you can see and use this cloud collection. The next step
invites the rest of your team.

## Step 4: Invite your team

1. Still in **Settings > Team Collection**, you'll now see a list of people
   approved to use this collection (just you, so far), with an option to add more.
2. Enter each team member's email address and choose their role (**Member** or
   **Administrator**). Use the email address they'll sign in to Bloom with.
3. Repeat for everyone on your team.

You don't need to invite people one at a time and wait — add everyone now, and
they can each join whenever they're ready.

## Step 5: Each team member joins

Each invited team member does this, once, on their own computer:

1. Turn on the **Cloud Team Collections (experimental)** feature, as in Step 1
   above (if not already on), and restart Bloom.
2. On Bloom's startup screen (the collection chooser), look for **Get my Team
   Collections** on the right-hand side.
3. Sign in with the same email address the administrator used to invite you.
4. Your collection should appear in the list. Click it to pull it down.
5. Bloom downloads a local copy and opens it automatically.

### If you already have a local copy of this collection

If you (or your team) had local copies of some of these books before switching to
the cloud — for example, from before you un-teamed the old shared collection —
Bloom will offer to merge your existing local collection with the cloud one instead
of starting fresh, but this may replace divergent local copies of a book with
whatever is in the cloud. This is one more reason the "everyone checks in first"
step matters: it means everyone's local copy already matches what got uploaded to
the cloud, so there's nothing to lose.

## Troubleshooting

- **"There is already a different Team Collection... on this computer"** — this
  computer has a `TeamCollectionLink.txt` left over from Step 2 that wasn't fully
  removed, or it's already linked to some other Team Collection with the same
  name. Check the folder and remove or rename the conflicting collection folder,
  then try again.
- **The "Share this collection" button is disabled or missing** — check that
  **Cloud Team Collections (experimental)** is turned on (Step 1) and that you've
  restarted Bloom since turning it on.
- **A team member can't see the collection under "Get my Team Collections"** —
  double check the administrator invited the exact email address that person signs
  in with, and that they've actually signed in (not just opened the sign-in
  dialog).
- Still stuck? Use Bloom's **Report a Problem** button — this feature is
  experimental and the Bloom team wants to hear about anything that doesn't work
  as described here.
