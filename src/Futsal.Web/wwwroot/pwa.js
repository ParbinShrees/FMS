if ('serviceWorker' in navigator) navigator.serviceWorker.register('/service-worker.js');

window.futsalSubmitForm=id=>{if(id==="esewaForm")document.getElementById(id)?.requestSubmit();};

window.futsalInviteToken=()=>{const token=location.hash.slice(1);history.replaceState(null,"",location.pathname);return token;};
