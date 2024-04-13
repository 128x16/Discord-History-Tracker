// noinspection FunctionWithInconsistentReturnsJS
const STATE = (function() {
	let serverPort = -1;
	let serverToken = "";
	
	const post = function(endpoint, json) {
		const aborter = new AbortController();
		const timeout = window.setTimeout(() => aborter.abort(), 5000);
		
		return new Promise(async (resolve, reject) => {
			let r;
			try {
				r = await fetch("http://127.0.0.1:" + serverPort + endpoint, {
					method: "POST",
					headers: {
						"Content-Type": "application/json",
						"X-DHT-Token": serverToken
					},
					credentials: "omit",
					redirect: "error",
					body: JSON.stringify(json),
					signal: aborter.signal
				});
			} catch (e) {
				if (e.name === "AbortError") {
					reject({ status: "DISCONNECTED" });
					return;
				}
				else {
					reject({ status: "ERROR", message: e.message });
					return;
				}
			} finally {
				window.clearTimeout(timeout);
			}
			
			if (r.status === 200) {
				resolve(r);
				return;
			}
			
			try {
				const message = await r.text();
				reject({ status: "ERROR", message });
			} catch (e) {
				reject({ status: "ERROR", message: e.message });
			}
		});
	};
	
	const getDate = function(date) {
		if (date instanceof Date) {
			return date;
		}
		else {
			// noinspection JSUnresolvedReference
			return date.toDate();
		}
	};
	
	const trackingStateChangedListeners = [];
	let isTracking = false;
	
	const addedChannels = new Set();
	const addedUsers = new Set();
	
	/**
	 * @name DiscordUser
	 * @property {String} id
	 * @property {String} username
	 * @property {String} discriminator
	 * @property {String} [avatar]
	 * @property {Boolean} [bot]
	 */
	
	/**
	 * @name DiscordMessage
	 * @property {String} id
	 * @property {String} channel_id
	 * @property {DiscordUser} author
	 * @property {String} content
	 * @property {Date} timestamp
	 * @property {Date|null} editedTimestamp
	 * @property {DiscordAttachment[]} attachments
	 * @property {Object[]} embeds
	 * @property {DiscordMessageReaction[]} [reactions]
	 * @property {DiscordMessageReference} [messageReference]
	 * @property {Number} type
	 * @property {String} state
	 */
	
	/**
	 * @name DiscordAttachment
	 * @property {String} id
	 * @property {String} filename
	 * @property {String} [content_type]
	 * @property {String} size
	 * @property {String} url
	 */
	
	/**
	 * @name DiscordMessageReaction
	 * @property {DiscordEmoji} emoji
	 * @property {Number} count
	 */
	
	/**
	 * @name DiscordMessageReference
	 * @property {String} [message_id]
	 */
	
	/**
	 * @name DiscordEmoji
	 * @property {String|null} id
	 * @property {String|null} name
	 * @property {Boolean} animated
	 */
	
	return {
		setup(port, token) {
			serverPort = port;
			serverToken = token;
		},
		
		onTrackingStateChanged(callback) {
			trackingStateChangedListeners.push(callback);
			callback(isTracking);
		},
		
		isTracking() {
			return isTracking;
		},
		
		setIsTracking(state) {
			if (isTracking !== state) {
				isTracking = state;
				
				if (isTracking) {
					addedChannels.clear();
					addedUsers.clear();
				}
				
				for (const callback of trackingStateChangedListeners) {
					callback(isTracking);
				}
			}
		},
		
		async addDiscordChannel(serverInfo, channelInfo) {
			if (addedChannels.has(channelInfo.id)) {
				return;
			}
			
			const server = {
				id: serverInfo.id,
				name: serverInfo.name,
				type: serverInfo.type
			};
			
			const channel = {
				id: channelInfo.id,
				name: channelInfo.name
			};
			
			if ("extra" in channelInfo) {
				const extra = channelInfo.extra;
				
				if ("parent" in extra) {
					channel.parent = extra.parent;
				}
				
				channel.position = extra.position;
				channel.topic = extra.topic;
				channel.nsfw = extra.nsfw;
			}
			
			await post("/track-channel", { server, channel });
			addedChannels.add(channelInfo.id);
		},
		
		/**
		 * @param {DiscordMessage[]} discordMessageArray
		 */
		async addDiscordMessages(discordMessageArray) {
			discordMessageArray = discordMessageArray.filter(msg => (msg.type === DISCORD.MESSAGE_TYPE.DEFAULT || msg.type === DISCORD.MESSAGE_TYPE.REPLY || msg.type === DISCORD.MESSAGE_TYPE.THREAD_STARTER) && msg.state === "SENT");
			
			if (discordMessageArray.length === 0) {
				return false;
			}
			
			const userInfo = {};
			let hasNewUsers = false;
			
			for (const msg of discordMessageArray) {
				const user = msg.author;
				
				if (!addedUsers.has(user.id)) {
					const obj = {
						id: user.id,
						name: user.username
					};
					
					if (user.avatar) {
						obj.avatar = user.avatar;
					}
					
					if (!user.bot) {
						// noinspection JSUnusedGlobalSymbols
						obj.discriminator = user.discriminator;
					}
					
					userInfo[user.id] = obj;
					hasNewUsers = true;
				}
			}
			
			if (hasNewUsers) {
				await post("/track-users", Object.values(userInfo));
				
				for (const id of Object.keys(userInfo)) {
					addedUsers.add(id);
				}
			}
			
			const response = await post("/track-messages", discordMessageArray.map(msg => {
				const obj = {
					id: msg.id,
					sender: msg.author.id,
					channel: msg.channel_id,
					text: msg.content,
					timestamp: getDate(msg.timestamp).getTime()
				};
				
				if (msg.editedTimestamp !== null) {
					// noinspection JSUnusedGlobalSymbols
					obj.editTimestamp = getDate(msg.editedTimestamp).getTime();
				}
				
				if (msg.messageReference !== null) {
					// noinspection JSUnusedGlobalSymbols
					obj.repliedToId = msg.messageReference.message_id;
				}
				
				if (msg.attachments.length > 0) {
					obj.attachments = msg.attachments.map(attachment => {
						const mapped = {
							id: attachment.id,
							name: attachment.filename,
							size: attachment.size,
							url: attachment.url
						};
						
						if (attachment.content_type) {
							mapped.type = attachment.content_type;
						}
						
						if (attachment.width && attachment.height) {
							mapped.width = attachment.width;
							mapped.height = attachment.height;
						}
						
						return mapped;
					});
				}
				
				if (msg.embeds.length > 0) {
					obj.embeds = msg.embeds.map(embed => {
						const mapped = {};
						
						for (const key of Object.keys(embed)) {
							if (key === "id") {
								continue;
							}
							
							if (key === "rawTitle") {
								mapped["title"] = embed[key];
							}
							else if (key === "rawDescription") {
								mapped["description"] = embed[key];
							}
							else {
								mapped[key] = embed[key];
							}
						}
						
						return JSON.stringify(mapped);
					});
				}
				
				if (msg.poll) {
					obj.poll = {
						question: msg.poll.question.text,
						answers: msg.poll.answers.map(answer => {
							console.log(answer);
							const mapped = {
								id: answer.answer_id,
								text: answer.poll_media.text
							};
							
							if (answer.poll_media.emoji) {
								const emoji = answer.poll_media.emoji;
								mapped.emoji = {};
								
								if (emoji.id) {
									mapped.emoji.id = String(emoji.id);
								}
								
								if (emoji.name) {
									mapped.emoji.name = emoji.name;
								}
								
								if (emoji.animated) {
									// noinspection JSUnusedGlobalSymbols
									mapped.emoji.isAnimated = emoji.animated;
								}
							}
							
							return mapped;
						}),
						multiSelect: msg.poll.allow_multiselect,
						expiryTimestamp: getDate(msg.poll.expiry).getTime()
					}
				}
				
				if (msg.reactions.length > 0) {
					obj.reactions = msg.reactions.map(reaction => {
						const emoji = reaction.emoji;
						
						const mapped = {};

						if (msg.poll) {
							mapped.count = reaction.count_details.vote;
						} else {
							mapped.count = reaction.count;
						}
						
						if (emoji.id) {
							mapped.id = String(emoji.id);
						}
						
						if (emoji.name) {
							mapped.name = emoji.name;
						}
						
						if (emoji.animated) {
							// noinspection JSUnusedGlobalSymbols
							mapped.isAnimated = emoji.animated;
						}
						
						return mapped;
					});
				}
				
				return obj;
			}));
			
			const anyNewMessages = await response.text();
			return anyNewMessages === "1";
		}
	};
})();
