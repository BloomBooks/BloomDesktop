/**
 * Finds the GlobalObject on the main document
 */
function getGlobalObject() {

	if (typeof document['globalObject'] === 'object') {
		return document['globalObject'];
	}
	else if (typeof window.parent['globalObject'] === 'object') {
		return window.parent['globalObject'];
	}

	// not found
	return null
}
