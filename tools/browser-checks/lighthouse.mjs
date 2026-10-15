import lighthouse from 'lighthouse';
import * as chromeLauncher from 'chrome-launcher';
import fs from 'node:fs/promises';
const url=process.env.PUBLIC_URL;if(!url)throw new Error('PUBLIC_URL is required');
const chrome=await chromeLauncher.launch({chromeFlags:['--headless','--no-sandbox'],chromePath:process.env.CHROME_PATH});
try{const result=await lighthouse(url,{port:chrome.port,extraHeaders:process.env.LOCAL_TEST_PROXY==='true'?{'X-Forwarded-For':'127.0.0.1','X-Forwarded-Proto':'https'}:{},output:['json','html'],logLevel:'error',formFactor:'mobile',screenEmulation:{mobile:true,width:390,height:844,deviceScaleFactor:1},throttlingMethod:'simulate'});await fs.mkdir('../../artifacts/lighthouse',{recursive:true});await fs.writeFile('../../artifacts/lighthouse/report.json',result.report[0]);await fs.writeFile('../../artifacts/lighthouse/report.html',result.report[1]);const p=result.lhr.categories.performance.score,a=result.lhr.categories.accessibility.score;console.log(JSON.stringify({performance:p,accessibility:a}));if(p<.8||a<.9)process.exitCode=1;}finally{await chrome.kill();}
