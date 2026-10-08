import test from 'node:test';
import assert from 'node:assert/strict';
import {nearestPlane,avoidLabelOverlap} from '../../web-frontend/src/services/scene-view.js';

test('snaps near all three planes including reverse views, leaves diagonal views free',()=>{
 assert.deepEqual(nearestPlane({x:.1,y:1,z:.1}),{view:'top',sign:1});
 assert.deepEqual(nearestPlane({x:1,y:.1,z:0}),{view:'side',sign:1});
 assert.deepEqual(nearestPlane({x:0,y:.1,z:-1}),{view:'front',sign:-1});
 assert.equal(nearestPlane({x:1,y:1,z:1}),null);
});
test('labels retain full width and never overlap or cross viewport edges',()=>{
 const labels=avoidLabelOverlap([{x:100,y:30,width:180},{x:120,y:30,width:100},{x:100,y:65,width:180},{x:3,y:90,width:100}],300,100);
 assert.equal(labels.length,2);
 assert.equal(labels[0].width,180);
 assert.ok(labels[1].rect.top>labels[0].rect.bottom);
});
