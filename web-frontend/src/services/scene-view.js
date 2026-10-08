export function nearestPlane({x,y,z}, threshold = 12) {
  const length = Math.hypot(x,y,z);
  if (!length) return null;
  const choices = [{view:'top',value:y}, {view:'front',value:z}, {view:'side',value:x}];
  const best = choices.sort((a,b)=>Math.abs(b.value)-Math.abs(a.value))[0];
  return Math.abs(best.value)/length >= Math.cos(threshold*Math.PI/180)
    ? {view:best.view,sign:Math.sign(best.value)} : null;
}

export function avoidLabelOverlap(items, width, height) {
  const placed=[];
  for (const item of items) {
    const left=item.x-item.width/2, top=item.y-11;
    if(left<6 || top<6 || left+item.width>width-6 || top+22>height-6) continue;
    const rect={left,top,right:left+item.width,bottom:top+22};
    if(placed.some(p=>rect.left<p.rect.right+6 && rect.right>p.rect.left-6 && rect.top<p.rect.bottom+4 && rect.bottom>p.rect.top-4)) continue;
    placed.push({...item,rect});
  }
  return placed;
}
