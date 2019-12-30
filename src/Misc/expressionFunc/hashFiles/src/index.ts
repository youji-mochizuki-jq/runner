import * as glob from '@actions/glob'

async function run(): Promise<void> {
    var matchPattern = process.argv[2];
    console.log("Match Pattern: " + matchPattern);

    var matchedFiles = await glob.glob(matchPattern);

    console.error("__OUTPUT__" + Buffer.from(JSON.stringify(matchedFiles)).toString('base64') + "__OUTPUT__");
}

run()
