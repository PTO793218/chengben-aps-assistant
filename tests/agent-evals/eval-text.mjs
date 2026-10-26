// The policy forbids asserting these labels, while an explicit rejection is valid.
export function hasForbiddenAssertion(text,term){
  if(!['孤立任务','异常'].includes(term))return text.includes(term);
  const plain=text.replace(/[*_`“”"‘’']/g,'');
  for(let at=plain.indexOf(term);at>=0;at=plain.indexOf(term,at+term.length)){
    const prefix=plain.slice(Math.max(0,at-40),at).trimEnd();
    const rejection=/(?:不能|不得|不应|不可|无法)(?:直接|据此)?(?:称为|称作|认定为|判断为|判定为|判为|归类为|说成|说是|断定是否为)(?:(?:孤立任务|(?:系统)?异常)(?:或|、))?(?:系统)?\s*$/.test(prefix);
    const distinction=/(?:并非|不是|不(?:表示|代表|意味着|等同于|等于))(?:(?:从未生成|从未派工|孤立任务|(?:系统)?异常)(?:或|、))?(?:系统)?\s*$/.test(prefix);
    if(!rejection&&!distinction)return true;
  }
  return false;
}
