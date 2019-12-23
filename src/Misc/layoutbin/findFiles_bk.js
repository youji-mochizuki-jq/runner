#!/usr/bin/env node
// Copyright (c) GitHub. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
var fs = require('fs')
var path = require("path")

var _FindItem = (function () {
    function _FindItem(path, level) {
        this.path = path;
        this.level = level;
    }
    return _FindItem;
}());

let output = {
    files: [],
    logs: []
};

output.logs = [];
output.files = [];
// normalize the path, otherwise the first result is inconsistently formatted from the rest of the results
// because path.join() performs normalization.
let findPath = process.cwd();
// debug trace the parameters
output.logs.push(`findPath: '${findPath}'`);
// return empty if not exists
try {
    fs.lstatSync(findPath);
}
catch (err) {
    if (err.code == 'ENOENT') {
        output.logs.push('0 results');
        console.error(Buffer.from(JSON.stringify(output)).toString('base64'));
        return
    } else {
        output.logs.push(err.message);
        console.error(Buffer.from(JSON.stringify(output)).toString('base64'));
        return
    }
}
try {
    // push the first item
    var stack = [new _FindItem(findPath, 1)];
    var traversalChain = []; // used to detect cycles
    var _loop_1 = function () {
        // pop the next item and push to the result array
        var item = stack.pop();
        // stat the item.  the stat info is used further below to determine whether to traverse deeper
        //
        // stat returns info about the target of a symlink (or symlink chain),
        // lstat returns info about a symlink itself
        var stats_2 = void 0;
        // use stat (following all symlinks)
        stats_2 = fs.statSync(item.path);
        // note, isDirectory() returns false for the lstat of a symlink
        if (stats_2.isDirectory()) {
            console.log("  " + item.path + " (directory)");
            // get the realpath
            var realPath_1 = fs.realpathSync(item.path);
            // fixup the traversal chain to match the item level
            while (traversalChain.length >= item.level) {
                traversalChain.pop();
            }
            // test for a cycle
            if (traversalChain.some(function (x) { return x == realPath_1; })) {
                output.logs.push('    cycle detected:' + realPath_1 + " -> " + item.path);
                return "continue";
            }
            // update the traversal chain
            traversalChain.push(realPath_1);
            // push the child items in reverse onto the stack
            var childLevel_1 = item.level + 1;
            var childItems = fs.readdirSync(item.path)
                .map(function (childName) { return new _FindItem(path.join(item.path, childName), childLevel_1); });
            stack.push.apply(stack, childItems.reverse());
        }
        else {
            console.log("  " + item.path + " (file)");
            output.files.push(item.path);
        }
    };
    while (stack.length) {
        var state_1 = _loop_1();
        if (state_1 === "continue") continue;
    }
    output.logs.push(output.files.length + " results");
    console.error(Buffer.from(JSON.stringify(output)).toString('base64'));
}
catch (err) {
    output.logs.push(err.message);
    console.error(Buffer.from(JSON.stringify(output)).toString('base64'));
}
