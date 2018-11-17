"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/callActivityHub").build();

function appendActivityList(cid, speech) {
    var msg = speech.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    var encodedMsg = `[${new Date().toISOString()}] ${cid} ${msg}`;
    var li = document.createElement("li");
    li.textContent = encodedMsg;
    document.getElementById("messagesList").appendChild(li);
}

connection.on("SendSpeech", function (cid, speech) {
    appendActivityList(cid, `- "${speech}"`);
});

connection.on("SendAction", function (cid, speech) {
    appendActivityList(cid, speech);
});

connection.on("SendShortAction", function (action) {
    appendActivityList("", action);
});

connection.start().catch(function (err) {
    return console.error(err.toString());
});
