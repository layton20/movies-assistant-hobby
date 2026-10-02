// One user message just over the API's 2,000 character limit
module.exports = () => ({ output: 'a'.repeat(2001) });
