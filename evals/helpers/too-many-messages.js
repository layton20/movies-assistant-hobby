// 1001 alternating messages (user first and last): only the API's 1000-message cap is violated
module.exports = () => ({
  output: JSON.stringify(
    Array.from({ length: 1001 }, (_, i) => ({
      role: i % 2 === 0 ? 'user' : 'assistant',
      content: 'hi',
    })),
  ),
});
