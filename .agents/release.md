# The release

> How a version of LUI reaches nuget.org (ADR-0048). The pipeline is `.github/workflows/publish.yml`; the owner does the
> five steps of the first section **once**, and the four of the second **for every release**.

## Once

1. **Make the repository public** — *Settings → General → Danger Zone → Change visibility*. The environment with a
   required reviewer (step 2) needs it on the free plan, and SourceLink needs it so that the users of the package can step
   into the source.
2. **Create the environment `nuget`** — *Settings → Environments → New environment*, name `nuget`:
   - *Required reviewers*: the owner;
   - *Deployment branches and tags*: *Selected branches and tags* → add the tag rule `v*`.
3. **Add the policy on nuget.org** — log in, click the user name → *Trusted Publishing* → *Add policy*:
   - *Repository Owner* `nordmanndev`, *Repository* `LUI`, *Workflow File* `publish.yml` (the name only),
     *Environment* `nuget`;
   - the scope must allow **new packages** — `Nordmann.LUI` and `Nordmann.LUI.Hosting` do not exist on nuget.org yet
     (a glob `Nordmann.LUI*` covers both).
4. **Set the variable `NUGET_USER`** — *Settings → Secrets and variables → Actions → Variables*: the **user name** of the
   nuget.org account (the profile name, not the e-mail address).
5. **After the first release went out: delete the secret `NUGET_API_KEY`** — *Settings → Secrets and variables → Actions*.
   The pipeline does not read it any more; a key that is not needed is a key that can leak.

## For every release

1. **`main` is green**, and the CHANGELOG has the section of the version (`## [0.1.0] — …`) with its date.
2. **The version** in `Directory.Build.props` (`VersionPrefix`) is the version of the release — the pipeline refuses a tag
   that disagrees.
3. **Tag the commit of `main`** and push the tag:

   ```bash
   git switch main && git pull
   git tag -a v0.1.0 -m "LUI 0.1.0"
   git push origin v0.1.0
   ```

4. **Approve the push** — *Actions → Publish → the run → Review deployments → nuget → Approve*. It is asked for after
   the gates of every pull request are green again on the tagged commit (about five minutes).

A tag on a commit that is not on `main` fails at once (*The tag is a release of main*). A failed gate fails the release
before anyone is asked. The packages of every release stay as an artifact of its run for 90 days.
